# Component-Level Dirty Tracking

## Quick Start

### 1. Implement a Component with Dirty Tracking

```csharp
using V12.Interfaces;

public class HealthComponent : IComponent
{
    private float _health = 100f;
    
    // Required event for dirty tracking
    public event Action<IComponent>? OnDirty;
    
    public long Id { get; private set; }
    public long? EntityId { get; set; }
    public string Name => "Health";
    public string Description => "Player health";

    public float Health
    {
        get => _health;
        set
        {
            if (_health != value)
            {
                _health = value;
                RaiseOnDirty(); // Trigger network sync
            }
        }
    }

    // Helper method to raise the dirty event
    protected void RaiseOnDirty()
    {
        OnDirty?.Invoke(this);
    }

    // ... implement other IComponent members
}
```

### 2. Setup DirtyTracker in Your Application

```csharp
// In Program.Main or initialization code
var dirtyTracker = new DirtyTracker(NetworkCables.Default);

// Configure throttling (send updates every 100ms)
dirtyTracker.ThrottleInterval = 0.1f;  
dirtyTracker.MaxBatchSize = 50;

// Track an entire world (automatically tracks all elements and components)
dirtyTracker.TrackWorld(gameRoot.SelectedWorld);

// In your update loop
dirtyTracker.Update(deltaTime);
```

### 3. Receive and Apply Updates

```csharp
NetworkCables.Default.OnMessageReceived += (message) =>
{
    if (message.MessageType == MessageType.WorldUpdate)
    {
        try
        {
            var batch = AncientCompressor.Decompress<ComponentBatchDTO>(message.Message);
            
            foreach (var component in batch.Components)
            {
                // Find the local component by ID and update it
                ApplyComponentUpdate(component);
            }
        }
        catch { /* handle element batches */ }
    }
};
```

## How It Works

### Event Flow
```
Component Property Change
    ↓
MarkDirty() / RaiseOnDirty()
    ↓
OnDirty Event Raised
    ↓
DirtyTracker Captures Change
    ↓
Batches with Other Changes
    ↓
Throttled Send (e.g., every 100ms)
    ↓
NetworkCables.SendQueue
    ↓
Network Transport (TCP)
    ↓
Receiving Client
    ↓
NetworkCables.ReceiveQueue
    ↓
OnMessageReceived Event
    ↓
Apply Update to Local World
```

### Batching Benefits
- **Efficiency**: Multiple changes batched into one network message
- **Bandwidth**: Only sends changed components, not entire world
- **Throttling**: Prevents network spam during high-change scenarios
- **Reliability**: Messages delivered in order via TCP

## Performance Tips

### 1. Use Threshold Checks for Floating Point
```csharp
public float Position
{
    get => _position;
    set
    {
        // Only mark dirty if change is significant
        if (Math.Abs(_position - value) > 0.001f)
        {
            _position = value;
            RaiseOnDirty();
        }
    }
}
```

### 2. Batch Multiple Property Changes
```csharp
public void SetTransform(float x, float y, float z, float rotation)
{
    _x = x;
    _y = y;
    _z = z;
    _rotation = rotation;
    
    // Call MarkDirty once after all changes
    RaiseOnDirty();
}
```

### 3. Selective Tracking
```csharp
// Only track components that need network sync
dirtyTracker.TrackComponent(playerTransform);
dirtyTracker.TrackComponent(playerHealth);

// Don't track purely visual/local components
// dirtyTracker.TrackComponent(particleEffect); // NO
```

### 4. Adjust Throttle Based on Use Case
```csharp
// Fast-paced action game (30Hz updates)
dirtyTracker.ThrottleInterval = 0.033f;

// Slower-paced strategy game (5Hz updates)
dirtyTracker.ThrottleInterval = 0.2f;

// Immediate update (no throttling)
dirtyTracker.ThrottleInterval = 0f;
```

## Advanced: Custom Batch Serialization

For maximum efficiency, implement custom serialization for your components:

```csharp
public class CompactTransformDTO
{
    public long ComponentId { get; set; }
    public short X { get; set; }  // Scaled int16 instead of float
    public short Y { get; set; }
    public short Z { get; set; }
    public byte Rotation { get; set; }  // 0-255 = 0-360 degrees
}
```

This can reduce a 40-byte float-based transform to 11 bytes.

## Testing

Run the included xUnit tests to see dirty tracking in action:

```bash
dotnet test V12.Headlesstests/V12.Headlesstests.csproj --filter "FullyQualifiedName~DirtyTrackingTests"
```

This demonstrates:
- Component property changes triggering dirty events
- Batching multiple changes
- Throttled network sends
- Element-level dirty tracking

## API Reference

### DirtyTracker

**Constructor**
```csharp
new DirtyTracker(NetworkCables? cables = null, string? senderUri = null)
```

**Properties**
- `ThrottleInterval` (float): Seconds between batched sends
- `MaxBatchSize` (int): Max items per network message
- `PendingComponentCount` (int): Current dirty components
- `PendingElementCount` (int): Current dirty elements

**Methods**
- `TrackWorld(World world)`: Track all elements and components in world
- `TrackElement(IWorldElement element)`: Track single element and its components
- `TrackComponent(IComponent component)`: Track single component
- `UntrackElement/UntrackComponent`: Stop tracking
- `Update(float deltaTime)`: Call in update loop for throttled sends
- `SendDirtyUpdates()`: Immediate send (ignores throttle)
- `Clear()`: Clear pending without sending

### IComponent.OnDirty Event
```csharp
event Action<IComponent>? OnDirty;
```
Raised when component state changes. Subscribe via `DirtyTracker.TrackComponent()`.

### IWorldElement.OnDirty Event
```csharp
event Action<IWorldElement>? OnDirty;
```
Raised when element state changes. Subscribe via `DirtyTracker.TrackElement()`.

## See Also
- [NETWORKING_GUIDE.md](NETWORKING_GUIDE.md) - Full networking documentation
- [TransformComponent.cs](Components/TransformComponent.cs) - Example implementation
- [DirtyTrackingTests.cs](../V12.Headlesstests/DirtyTrackingTests.cs) - xUnit tests
- [V12.Headlesstests README](../V12.Headlesstests/README.md) - Test project documentation
