using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// TEMA ACASĂ: Camera orbită privește mereu spre un punct (ținta) și se mișcă pe o sferă
/// în jurul lui, parametrizată prin distanță, azimut și elevație.
/// </summary>
public class OrbitCamera : Camera
{
    public Vector3 Target { get; set; }
    public float Distance { get; private set; }
    public float Azimuth { get; private set; }     // grade, în jurul axei Y
    public float Elevation { get; private set; }   // grade, deasupra planului orizontal

    public const float MaxElevation = 89.0f;
    public const float MinDistance = 1.0f;
    public const float MaxDistance = 40.0f;

    public float MouseSensitivity = 0.25f;         // grade pe pixel la drag cu clic stânga
    public float KeyRotateSpeed = 90.0f;           // grade pe secundă cu săgețile
    public float ZoomStep = 1.0f;                  // unități de distanță pe pas de rotiță
    public float PanSpeed = 5.0f;                  // unități pe secundă cu W A S D (bonus)

    private static readonly Vector3 WorldUp = Vector3.UnitY;
    private static readonly Vector3 StartTarget = new(0.0f, 1.2f, -2.0f);
    private const float StartDistance = 8.5f;
    private const float StartAzimuth = 90.0f;
    private const float StartElevation = 15.0f;

    public OrbitCamera()
    {
        Reset();
    }

    public override string Name => "Orbită";

    public override Vector3 Position
    {
        get
        {
            float azRad = MathHelper.DegreesToRadians(Azimuth);
            float elRad = MathHelper.DegreesToRadians(Elevation);

            Vector3 offset = Distance * new Vector3(
                MathF.Cos(elRad) * MathF.Cos(azRad),
                MathF.Sin(elRad),
                MathF.Cos(elRad) * MathF.Sin(azRad));

            return Target + offset;
        }
    }

    public override Vector3 Front => Vector3.Normalize(Target - Position);

    public override Matrix4 ViewMatrix =>
        Matrix4.LookAt(Position, Target, WorldUp);

    public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
    {
        // 1. Rotație cu clic stânga ținut apăsat + mișcarea mouse-ului
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            Azimuth += mouseDelta.X * MouseSensitivity;
            Elevation += mouseDelta.Y * MouseSensitivity;
        }

        // 2. Rotație de la tastatură cu săgețile
        if (keyboard.IsKeyDown(Keys.Left))  Azimuth += KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Right)) Azimuth -= KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Up))    Elevation += KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Down))  Elevation -= KeyRotateSpeed * dt;

        // Limitarea elevației la ±89° pentru a evita blocarea cardanică / răsturnarea imaginii
        Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);

        // 3. Rotița mouse-ului modifică distanța (nu FOV-ul)
        if (mouse.ScrollDelta.Y != 0.0f)
        {
            Distance = Math.Clamp(Distance - mouse.ScrollDelta.Y * ZoomStep, MinDistance, MaxDistance);
        }

        // 4. Bonus 2: W A S D mută ținta în planul orizontal
        Vector3 forward = new Vector3(Front.X, 0.0f, Front.Z);
        if (forward.LengthSquared > 0.0f)
        {
            forward = Vector3.Normalize(forward);
        }
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, WorldUp));

        Vector3 move = Vector3.Zero;
        if (keyboard.IsKeyDown(Keys.W)) move += forward;
        if (keyboard.IsKeyDown(Keys.S)) move -= forward;
        if (keyboard.IsKeyDown(Keys.D)) move += right;
        if (keyboard.IsKeyDown(Keys.A)) move -= right;

        if (move.LengthSquared > 0.0f)
        {
            move = Vector3.Normalize(move);
            float speed = keyboard.IsKeyDown(Keys.LeftShift) ? PanSpeed * 2.0f : PanSpeed;
            Target += move * speed * dt;
        }
    }

    /// <summary>
    /// Bonus 1 (FPS -> Orbită): poziționează camera orbită exact în poziția <paramref name="eyePosition"/>
    /// privind spre <paramref name="target"/>, fără salt vizual.
    /// </summary>
    public void SetFromEyeAndTarget(Vector3 eyePosition, Vector3 target)
    {
        Target = target;
        Vector3 offset = eyePosition - target;
        float dist = offset.Length;
        if (dist < 1e-4f)
        {
            dist = MinDistance;
            offset = Vector3.UnitZ * dist;
        }

        Distance = Math.Clamp(dist, MinDistance, MaxDistance);
        Vector3 o = offset / dist; // vectorul unitar țintă -> cameră

        float elRad = MathF.Asin(Math.Clamp(o.Y, -1.0f, 1.0f));
        float azRad = MathF.Atan2(o.Z, o.X);

        Elevation = Math.Clamp(MathHelper.RadiansToDegrees(elRad), -MaxElevation, MaxElevation);
        Azimuth = MathHelper.RadiansToDegrees(azRad);
    }

    public override void Reset()
    {
        Target = StartTarget;
        Distance = StartDistance;
        Azimuth = StartAzimuth;
        Elevation = StartElevation;
        FieldOfView = 60.0f;
    }

    public override IEnumerable<string> Describe()
    {
        Vector3 pos = Position;
        yield return $"Țintă          : ({Target.X,6:F2}, {Target.Y,6:F2}, {Target.Z,6:F2})";
        yield return $"Distanță       : {Distance,6:F2}";
        yield return $"Azimut / elev. : {NormalizeAngle(Azimuth),7:F1}° / {Elevation,6:F1}°";
        yield return $"Poziție        : ({pos.X,6:F2}, {pos.Y,6:F2}, {pos.Z,6:F2})";
    }

    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360.0f;
        if (degrees > 180.0f) degrees -= 360.0f;
        if (degrees <= -180.0f) degrees += 360.0f;
        return degrees;
    }
}
