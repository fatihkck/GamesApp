namespace GamesApp.Audio;

/// <summary>Piyano ses motoru sözleşmesi.</summary>
internal interface IPianoSound : IDisposable
{
    /// <summary>Ses motoru kullanılabilir durumda mı? (Ses aygıtı yoksa false.)</summary>
    bool IsAvailable { get; }

    /// <summary>Notayı çalmaya başlar.</summary>
    void NoteOn(int midiNote, int velocity);

    /// <summary>Notayı bırakır.</summary>
    void NoteOff(int midiNote);

    /// <summary>Çalan tüm notaları susturur.</summary>
    void AllNotesOff();
}
