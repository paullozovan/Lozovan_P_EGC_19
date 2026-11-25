using OpenTK.Mathematics;

namespace Egc;

/// <summary>
/// NOU: jucătorul din scena laboratorului (P4): un cub condus de la tastatură prin ACȚIUNI.
/// </summary>
public sealed class PlayerController
{
    public const float Speed = 4.0f;
    public const float Gravity = 20.0f;
    public const float MinJumpSpeed = 4.0f;
    public const float MaxJumpSpeed = 11.0f;
    public const float ChargeTime = 0.8f;

    private readonly SceneObject _body;
    private readonly SceneObject _shadow;
    private readonly Vector3 _start;
    private readonly float _groundY;
    private readonly Vector2 _min, _max;

    private Vector3 _position;
    private float _verticalSpeed;
    private bool _charging;
    private float _charge;

    public PlayerController(SceneObject body, SceneObject shadow, Vector2 min, Vector2 max)
    {
        _body = body;
        _shadow = shadow;
        _start = body.Transform.Position;
        _position = _start;
        _groundY = _start.Y;
        _min = min;
        _max = max;
    }

    public bool OnGround => _position.Y <= _groundY + 1e-4f;
    public Vector3 Position => _position;
    public float Charge => _charge;
    public float VerticalSpeed => _verticalSpeed;

    public void Update(float dt, Input input)
    {
        Vector3 p = _position;

        var direction = new Vector2(input.Axis(Actions.Left, Actions.Right),
                                    input.Axis(Actions.Forward, Actions.Back));
        if (direction.LengthSquared > 1.0f)
        {
            direction.Normalize();
        }
        p.X = Math.Clamp(p.X + direction.X * Speed * dt, _min.X, _max.X);
        p.Z = Math.Clamp(p.Z + direction.Y * Speed * dt, _min.Y, _max.Y);

        // TODO 6.5:
        if (OnGround && input.WasPressed(Actions.Jump))
        {
            _charging = true;
            _charge = 0.0f;
        }

        if (_charging && input.IsDown(Actions.Jump))
        {
            _charge = MathF.Min(_charge + dt / ChargeTime, 1.0f);
        }

        if (_charging && input.WasReleased(Actions.Jump))
        {
            _verticalSpeed = Easing.Lerp(MinJumpSpeed, MaxJumpSpeed, _charge);
            _charging = false;
            _charge = 0.0f;
        }

        if (!OnGround || _verticalSpeed > 0.0f)
        {
            _verticalSpeed -= Gravity * dt;
            p.Y += _verticalSpeed * dt;
            if (p.Y <= _groundY)
            {
                p.Y = _groundY;
                _verticalSpeed = 0.0f;
            }
        }
        _position = p;

        float squash = 0.35f * _charge;
        _body.Transform.Scale = new Vector3(1.0f + 0.5f * squash, 1.0f - squash, 1.0f + 0.5f * squash);
        _body.Transform.Position = p - new Vector3(0.0f, 0.5f * squash, 0.0f);
        _body.Tint = new Color4(0.3f + 0.7f * _charge, 0.85f, 1.0f - 0.7f * _charge, 1.0f);

        float height = p.Y - _groundY;
        float shadowSize = 1.1f / (1.0f + 0.25f * height);
        _shadow.Transform.Position = new Vector3(p.X, 0.01f, p.Z);
        _shadow.Transform.Scale = new Vector3(shadowSize, 1.0f, shadowSize);
    }

    public void Reset()
    {
        _position = _start;
        _body.Transform.Position = _start;
        _verticalSpeed = 0.0f;
        _charging = false;
        _charge = 0.0f;
    }

    public string State => _charging ? "încarcă saltul" : OnGround ? "pe sol" : "în aer";
}