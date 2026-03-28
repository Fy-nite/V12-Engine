# V12 Networking Setup Guide

## Overview
V12 now supports headless server hosting and client connections via TCP networking.

## Architecture
- **NetworkCables**: Thread-safe message queue system with event-based processing
- **NetworkHost**: TCP server that accepts multiple clients and broadcasts messages
- **NetworkClient**: TCP client that connects to a host and sends/receives messages
- **MessageDTO**: BSON-serialized message format with sender, type, and payload
- **DirtyTracker**: Batches and sends only changed components/elements for efficient network sync

## Component-Level Dirty Tracking

### Overview
The dirty tracking system automatically detects and synchronizes only changed components/elements, avoiding expensive full-world snapshots.

### How It Works
1. Components and world elements implement `OnDirty` events
2. When properties change, call `MarkDirty()` to raise the event
3. `DirtyTracker` listens to these events and batches changes
4. Batched updates are sent at a throttled rate (default: 10Hz)

### Implementing a Dirty-Tracked Component

```csharp
public class MyComponent : IComponent
{
    private float _health;

    public event Action<IComponent>? OnDirty;

    public float Health
    {
        get => _health;
        set
        {
            if (_health != value)
            {
                _health = value;
                MarkDirty(); // Automatically triggers network sync
            }
        }
    }

    // ... other IComponent members
}
```

### Using DirtyTracker

```csharp
// Initialize tracker
var tracker = new DirtyTracker(NetworkCables.Default);
tracker.ThrottleInterval = 0.1f;  // Send updates every 100ms
tracker.MaxBatchSize = 50;        // Max 50 items per batch

// Track a world (automatically tracks all elements and components)
tracker.TrackWorld(myWorld);

// Track individual components or elements
tracker.TrackComponent(myComponent);
tracker.TrackElement(myWorldElement);

// In your update loop
tracker.Update(deltaTime);

// Manual flush
tracker.SendDirtyUpdates();

// Stop tracking
tracker.UntrackComponent(myComponent);
tracker.UntrackElement(myWorldElement);
```

### Batch DTOs
Dirty updates are sent as batches for efficiency:

- **ComponentBatchDTO**: Contains multiple changed components
- **ElementBatchDTO**: Contains multiple changed world elements

These are automatically created by `DirtyTracker` and sent via `MessageType.WorldUpdate`.

### Performance Tips
1. **Throttle updates**: Adjust `ThrottleInterval` based on network conditions
2. **Batch size**: Increase `MaxBatchSize` for high-change scenarios
3. **Selective tracking**: Only track components that need synchronization
4. **Change detection**: Use threshold checks (e.g., `Math.Abs(old - new) > 0.001f`) to avoid micro-updates
5. **Batch property changes**: Change multiple properties then call `MarkDirty()` once

## Command-Line Arguments

### Start a Headless Server
```bash
dotnet run --project V12.GodotRuntime/V12.Headless.csproj -- --headless --port=7777
```
- `--headless`: Enables server mode
- `--port=<number>`: Optional port number (default: 7777)

### Connect as a Client
```bash
dotnet run --project V12.GodotRuntime/V12.Headless.csproj -- --connect=localhost --port=7777
```
- `--connect=<host>`: Connect to specified host (IP or hostname)
- `--port=<number>`: Port to connect to (default: 7777)

### Run Standalone (No Networking)
```bash
dotnet run --project V12.GodotRuntime/V12.Headless.csproj
```

## Usage Examples

### Sending Messages from Code
```csharp
// Option 1: Use the MarkDirty extension method (any object)
myWorldElement.MarkDirty(NetworkCables.Default, 
    new Uri("networkcables://myclient"), 
    MessageType.WorldUpdate);

// Option 2: Send a custom message directly
NetworkCables.Default.SendData(new MessageDTO
{
    Sender = new Uri("networkcables://sender"),
    MessageType = MessageType.Event,
    Message = AncientCompressor.Compress(myData)
});
```

### Receiving Messages
```csharp
// Subscribe to incoming messages
NetworkCables.Default.OnMessageReceived += (message) =>
{
    Console.WriteLine($"Received {message.MessageType} from {message.Sender?.AbsoluteUri}");
    
    // Deserialize based on message type
    switch (message.MessageType)
    {
        case MessageType.WorldUpdate:
            var worldData = AncientCompressor.Decompress<World>(message.Message);
            // Apply world update
            break;
        case MessageType.Event:
            // Handle event
            break;
    }
};
```

## Protocol Details

### Message Format (TCP)
1. **Length Prefix** (4 bytes): Int32 length of payload in bytes
2. **Payload** (variable): BSON-serialized MessageDTO

### Message Types
- `Text`: Text-based messages
- `Binary`: Raw binary data
- `Command`: Server commands
- `Event`: Generic events
- `WorldSync`: Full world state snapshot
- `WorldUpdate`: Delta/partial world updates
- `PlayerJoin/Leave/Ban/Kick/Action`: Player-related events
- `Error`: Error messages

## Thread Architecture
1. **Update Thread**: Runs game logic at ~1000Hz (1ms sleep)
2. **Networking Thread**: Processes incoming/outgoing message queues at ~100Hz (10ms sleep)
3. **Transport Threads**: Per-client async read/write loops (managed by NetworkHost/NetworkClient)

## Security Notes
⚠️ **Current Implementation**
- No authentication
- No encryption (plaintext TCP)
- No rate limiting
- 10MB message size limit (sanity check)

⚠️ **TODO for Production**
- Add TLS/SSL encryption
- Implement authentication tokens
- Add rate limiting per client
- Add message validation/schema enforcement
- Add heartbeat/timeout detection
- Implement reconnection logic

## Performance Considerations
- **Avoid sending full world snapshots every frame**: Use delta updates or dirty flags on entities/components
- **Batch small messages**: Collect multiple updates and send in one message
- **Throttle network frequency**: Current setup runs at 10Hz; adjust based on needs
- **Profile serialization cost**: BSON is convenient but may be slower than custom binary formats

## Next Steps
1. ✅ ~~Implement delta-based world synchronization (only send changed entities/components)~~ **DONE**
2. ✅ ~~Add component-level dirty tracking with `OnDirty` events~~ **DONE**
3. Implement proper client authentication
4. Add network statistics (latency, packet loss, bandwidth)
5. Add reconnection handling for dropped connections
6. Implement interpolation/prediction for smooth client updates
7. Add entity/component ID mapping for clients to apply updates to correct objects
8. Implement authority system (server authoritative vs client prediction)
