using System.Drawing;

namespace GamesApp.UI.Effects;

/// <summary>
/// Yukarı fırlayan, yer çekimiyle yavaşlayıp sönümlenen parçacık.
/// Yıldız veya daire olarak çizilir. Struct: allocation üretmez.
/// </summary>
internal struct Particle
{
    public PointF Position;
    public PointF Velocity;
    public float Age;
    public float Life;
    public float Size;
    public float Rotation;
    public float RotationSpeed;
    public Color Color;
    public bool IsStar;

    /// <summary>Yer çekimi ivmesi (piksel/saniye^2).</summary>
    private const float Gravity = 620f;

    public Particle(PointF position, PointF velocity, float life, float size, Color color, bool isStar, float rotation, float rotationSpeed)
    {
        Position = position;
        Velocity = velocity;
        Age = 0f;
        Life = life;
        Size = size;
        Color = color;
        IsStar = isStar;
        Rotation = rotation;
        RotationSpeed = rotationSpeed;
    }

    public bool IsAlive => Age < Life;

    /// <summary>0 (yeni) - 1 (bitmiş) arası normalize yaş.</summary>
    public float Progress => Life <= 0f ? 1f : Math.Clamp(Age / Life, 0f, 1f);

    public void Update(float deltaSeconds)
    {
        Age += deltaSeconds;
        Velocity = new PointF(Velocity.X, Velocity.Y + Gravity * deltaSeconds);
        Position = new PointF(
            Position.X + Velocity.X * deltaSeconds,
            Position.Y + Velocity.Y * deltaSeconds);
        Rotation += RotationSpeed * deltaSeconds;
    }
}
