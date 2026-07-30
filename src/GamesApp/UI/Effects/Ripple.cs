using System.Drawing;

namespace GamesApp.UI.Effects;

/// <summary>
/// Genişleyen renkli halka. Merkezden dışa doğru büyür, alfası azalır.
/// Struct olarak tanımlıdır: saniyede yüzlerce üretildiği için heap allocation istemiyoruz.
/// </summary>
internal struct Ripple
{
    /// <summary>Toplam ömür (saniye) - yaklaşık 900 ms.</summary>
    public const float LifeSeconds = 0.9f;

    public PointF Center;
    public float Radius;
    public float MaxRadius;
    public float Age;
    public Color Color;
    public float StartThickness;

    public Ripple(PointF center, float maxRadius, Color color, float startThickness)
    {
        Center = center;
        Radius = 8f;
        MaxRadius = maxRadius;
        Age = 0f;
        Color = color;
        StartThickness = startThickness;
    }

    /// <summary>Halka hâlâ görünür mü?</summary>
    public bool IsAlive => Age < LifeSeconds;

    /// <summary>0 (yeni) - 1 (bitmiş) arası normalize yaş.</summary>
    public float Progress => Math.Clamp(Age / LifeSeconds, 0f, 1f);

    public void Update(float deltaSeconds)
    {
        Age += deltaSeconds;

        // Başta hızlı, sonra yavaşlayan büyüme (ease-out).
        float p = Progress;
        float eased = 1f - (1f - p) * (1f - p);
        Radius = 8f + (MaxRadius - 8f) * eased;
    }
}
