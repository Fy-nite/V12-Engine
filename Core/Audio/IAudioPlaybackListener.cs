namespace V12.Core.Audio;

/// <summary>
/// Optional callback for audio sources that need to react when their channel
/// reaches the end of a non-looping clip. The game adapter calls this after it
/// has stopped and rewound the channel, so a playlist can advance to the next clip.
/// </summary>
public interface IAudioPlaybackListener
{
    void OnAudioEnded();
}
