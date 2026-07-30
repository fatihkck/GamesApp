using System.Drawing;

namespace GamesApp.Games.Bubbles;

/// <summary>
/// Ekranda yukarı doğru süzülen tek bir balon. Konum piksel cinsindendir; alan
/// yeniden boyutlandığında <see cref="Rescale"/> ile oranları korunarak taşınır.
/// </summary>
internal sealed class Balloon
{
    /// <summary>Yatay salınımın merkez ekseni (piksel).</summary>
    public float BaseX;

    /// <summary>Balonun gövde merkezinin dikey konumu (piksel).</summary>
    public float Y;

    /// <summary>Gövde yarıçapı (yatay yarı genişlik, piksel).</summary>
    public float Radius;

    /// <summary>Saniyedeki yükselme hızı (piksel).</summary>
    public float RiseSpeed;

    /// <summary>Yatay salınımın genliği (piksel).</summary>
    public float WobbleAmplitude;

    /// <summary>Yatay salınımın hızı (radyan/saniye).</summary>
    public float WobbleSpeed;

    /// <summary>Salınımın anlık fazı.</summary>
    public float WobblePhase;

    /// <summary>Gövde rengi (canlı, doygun).</summary>
    public Color Color;

    /// <summary>Balonun anlık yatay konumu (salınım dâhil).</summary>
    public float X => BaseX + (float)Math.Sin(WobblePhase) * WobbleAmplitude;

    /// <summary>Gövde yüksekliği yarıçapın bu katıdır (balonlar dikeyde uzundur).</summary>
    public float RadiusY => Radius * 1.18f;

    /// <summary>Zamanı ilerletir: yükselir ve salınır.</summary>
    public void Update(float deltaSeconds)
    {
        Y -= RiseSpeed * deltaSeconds;
        WobblePhase += WobbleSpeed * deltaSeconds;
    }

    /// <summary>Alan boyutu değiştiğinde balonu oransal olarak yeni alana taşır.</summary>
    public void Rescale(float scaleX, float scaleY)
    {
        BaseX *= scaleX;
        Y *= scaleY;
        Radius *= scaleY;
        RiseSpeed *= scaleY;
        WobbleAmplitude *= scaleX;
    }

    /// <summary>Verilen nokta balonun gövdesinde mi? (Fare ile patlatmak için.)</summary>
    public bool Contains(float px, float py)
    {
        float dx = (px - X) / Radius;
        float dy = (py - Y) / RadiusY;
        return dx * dx + dy * dy <= 1f;
    }
}
