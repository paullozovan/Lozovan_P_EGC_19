using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc.Teme;

/// <summary>
/// Varianta B: evită obstacolele.
/// Jucătorul aleargă pe un drum cu 3 benzi, schimbă banda cu A/D (sau săgeți) printr-un Tween
/// cu EaseOut, sare peste obstacolele joase cu Space/W/Up și repornește runda cu R.
/// </summary>
public sealed class TemaB_EvitaObstacolele : Scene
{
    // Acțiuni proprii scenei
    private const string LaneLeft = "banda stânga";
    private const string LaneRight = "banda dreaptă";
    private const string Jump = "salt peste obstacol";
    private const string Restart = "joc nou";

    // Geometria drumului și a benzilor
    private const float LaneWidth = 2.2f;
    private const float RoadWidth = LaneWidth * 3.4f;
    private const float RoadLength = 75.0f;
    private const float SpawnZ = -60.0f;
    private const float DespawnZ = 10.0f;

    // Dimensiuni și fizica jucătorului
    private const float PlayerHalfW = 0.35f;
    private const float PlayerHalfH = 0.65f;
    private const float PlayerHalfD = 0.35f;
    private const float JumpVelocity = 11.5f;
    private const float Gravity = 28.0f;

    // Starea jucătorului
    private int _currentLane; // -1 = stânga, 0 = centru, 1 = dreapta
    private Tween? _laneTween;
    private float _playerX;
    private float _playerY;
    private float _velocityY;
    private bool _isGrounded = true;
    private float _runCycle;

    // Dinamica lumii
    private float _worldSpeed;
    private float _spawnTimer;
    private float _spawnInterval;
    private float _score;
    private float _bestScore;
    private bool _gameOver;
    private readonly Random _rng = new();

    // Ierarhia de obiecte ale jucătorului (SceneObject cu Parent)
    private SceneObject _playerRoot = null!;
    private SceneObject _playerBody = null!;
    private SceneObject _playerHead = null!;
    private SceneObject _leftArm = null!;
    private SceneObject _rightArm = null!;
    private SceneObject _leftLeg = null!;
    private SceneObject _rightLeg = null!;
    private SceneObject _shadow = null!;

    // Marcaje rutiere (Bonus)
    private sealed class RoadStripe
    {
        public required SceneObject Obj { get; init; }
        public float Z { get; set; }
    }
    private readonly List<RoadStripe> _stripes = new();

    // Obstacole active
    private sealed class Obstacle
    {
        public required SceneObject Obj { get; init; }
        public int Lane { get; init; }
        public bool IsHigh { get; init; }
        public float Z { get; set; }
        public Vector3 HalfExtents { get; init; }
        public float CenterY { get; init; }
    }
    private readonly List<Obstacle> _obstacles = new();

    // Mesh reutilizat pentru obstacolele generate dinamic
    private Mesh _cubeMesh = null!;

    public override string Name => "Tema B - Evită obstacolele";

    public override IEnumerable<string> ActionNames => new[]
    {
        LaneLeft, LaneRight, Jump, Restart
    };

    public override string Status
    {
        get
        {
            string stare = _gameOver ? "COLIZIUNE! Apasă R pentru joc nou" : $"viteză: {_worldSpeed:F1} m/s";
            return $"scor: {_score:F1} s | record: {_bestScore:F1} s | {stare}";
        }
    }

    public override void Load(Input input)
    {
        CameraPosition = new Vector3(0.0f, 5.2f, 8.2f);
        CameraTarget = new Vector3(0.0f, 1.2f, -12.0f);

        // Legarea acțiunilor scenei (exclusiv prin Input, fără KeyboardState direct)
        input.Bind(LaneLeft, Keys.A, Keys.Left);
        input.Bind(LaneRight, Keys.D, Keys.Right);
        input.Bind(Jump, Keys.Space, Keys.W, Keys.Up);
        input.Bind(Restart, Keys.R);

        _cubeMesh = AddMesh(Shapes.ShadedCube());
        Mesh sphereMesh = AddMesh(Shapes.ShadedSphere(0.5f, 24, 12));
        Mesh diskMesh = AddMesh(Shapes.ShadedCylinder(0.5f, 0.02f, 24));

        BuildRoadAndStripes();
        BuildPlayerHierarchy(sphereMesh, diskMesh);
        ResetGame();
    }

    private void BuildRoadAndStripes()
    {
        // Suprafața drumului
        SceneObject road = AddObject("drum", _cubeMesh);
        road.Transform.Position = new Vector3(0.0f, -0.05f, -25.0f);
        road.Transform.Scale = new Vector3(RoadWidth, 0.1f, RoadLength);
        road.Tint = new Color4(0.20f, 0.22f, 0.26f, 1.0f);

        // Bordurile stânga / dreapta
        for (int side = -1; side <= 1; side += 2)
        {
            SceneObject curb = AddObject(side < 0 ? "bordură stânga" : "bordură dreapta", _cubeMesh);
            curb.Transform.Position = new Vector3(side * (RoadWidth * 0.5f), 0.12f, -25.0f);
            curb.Transform.Scale = new Vector3(0.35f, 0.24f, RoadLength);
            curb.Tint = new Color4(0.75f, 0.25f, 0.22f, 1.0f);
        }

        // Marcaje discontinue între benzi (se deplasează odată cu lumea)
        _stripes.Clear();
        float[] dividerX = { -LaneWidth * 0.5f, LaneWidth * 0.5f };
        const float stripeSpacing = 5.0f;
        for (float z = SpawnZ; z <= DespawnZ; z += stripeSpacing)
        {
            foreach (float x in dividerX)
            {
                SceneObject stripe = AddObject("marcaj", _cubeMesh);
                stripe.Transform.Position = new Vector3(x, 0.01f, z);
                stripe.Transform.Scale = new Vector3(0.12f, 0.02f, 2.2f);
                stripe.Tint = new Color4(0.92f, 0.92f, 0.85f, 1.0f);
                _stripes.Add(new RoadStripe { Obj = stripe, Z = z });
            }
        }
    }

    private void BuildPlayerHierarchy(Mesh sphereMesh, Mesh diskMesh)
    {
        _shadow = AddObject("umbră jucător", diskMesh);
        _shadow.Tint = new Color4(0.05f, 0.05f, 0.08f, 1.0f);

        // Pivotul principal al jucătorului (fără mesh, poartă toate părțile corpului)
        _playerRoot = AddObject("jucător pivot", null);

        _playerBody = AddObject("trunchi", _cubeMesh, _playerRoot);
        _playerBody.Transform.Position = new Vector3(0.0f, 0.72f, 0.0f);
        _playerBody.Transform.Scale = new Vector3(0.55f, 0.62f, 0.32f);
        _playerBody.Tint = new Color4(0.20f, 0.65f, 0.95f, 1.0f);

        _playerHead = AddObject("cap", sphereMesh, _playerRoot);
        _playerHead.Transform.Position = new Vector3(0.0f, 1.24f, 0.0f);
        _playerHead.Transform.Scale = new Vector3(0.42f);
        _playerHead.Tint = new Color4(0.96f, 0.80f, 0.66f, 1.0f);

        _leftArm = AddObject("braț stâng", _cubeMesh, _playerRoot);
        _leftArm.Transform.Position = new Vector3(-0.38f, 0.74f, 0.0f);
        _leftArm.Transform.Scale = new Vector3(0.16f, 0.52f, 0.16f);
        _leftArm.Tint = new Color4(0.16f, 0.52f, 0.80f, 1.0f);

        _rightArm = AddObject("braț drept", _cubeMesh, _playerRoot);
        _rightArm.Transform.Position = new Vector3(0.38f, 0.74f, 0.0f);
        _rightArm.Transform.Scale = new Vector3(0.16f, 0.52f, 0.16f);
        _rightArm.Tint = new Color4(0.16f, 0.52f, 0.80f, 1.0f);

        _leftLeg = AddObject("picior stâng", _cubeMesh, _playerRoot);
        _leftLeg.Transform.Position = new Vector3(-0.15f, 0.22f, 0.0f);
        _leftLeg.Transform.Scale = new Vector3(0.18f, 0.44f, 0.20f);
        _leftLeg.Tint = new Color4(0.15f, 0.22f, 0.35f, 1.0f);

        _rightLeg = AddObject("picior drept", _cubeMesh, _playerRoot);
        _rightLeg.Transform.Position = new Vector3(0.15f, 0.22f, 0.0f);
        _rightLeg.Transform.Scale = new Vector3(0.18f, 0.44f, 0.20f);
        _rightLeg.Tint = new Color4(0.15f, 0.22f, 0.35f, 1.0f);
    }

    private void ResetGame()
    {
        foreach (Obstacle obs in _obstacles)
        {
            Objects.Remove(obs.Obj);
        }
        _obstacles.Clear();

        _currentLane = 0;
        _playerX = 0.0f;
        _playerY = 0.0f;
        _velocityY = 0.0f;
        _isGrounded = true;
        _laneTween = null;

        _worldSpeed = 14.0f;
        _spawnInterval = 1.25f;
        _spawnTimer = 0.4f;
        _score = 0.0f;
        _gameOver = false;

        _playerBody.Tint = new Color4(0.20f, 0.65f, 0.95f, 1.0f);
        _playerRoot.Transform.Position = Vector3.Zero;
        _playerRoot.Transform.Rotation = Vector3.Zero;
        _shadow.Transform.Position = new Vector3(0.0f, 0.01f, 0.0f);
        _shadow.Transform.Scale = new Vector3(0.95f, 1.0f, 0.95f);
    }

    private void SwitchLane(int delta)
    {
        int targetLane = Math.Clamp(_currentLane + delta, -1, 1);
        if (targetLane == _currentLane) return;

        _currentLane = targetLane;
        float targetX = _currentLane * LaneWidth;
        // Tween cu EaseOut sub 0.2 s (0.16 s)
        _laneTween = new Tween(
            new Vector3(_playerX, 0.0f, 0.0f),
            new Vector3(targetX, 0.0f, 0.0f),
            0.16f,
            Easing.EaseOut);
    }

    private void SpawnObstacleWave()
    {
        int blockedCount = (_score > 12.0f && _rng.NextDouble() < 0.45) ? 2 : 1;
        int firstLane = _rng.Next(-1, 2);

        SpawnSingleObstacle(firstLane);

        if (blockedCount == 2)
        {
            int secondLane = firstLane + (_rng.Next(2) == 0 ? 1 : -1);
            if (secondLane < -1) secondLane = 1;
            if (secondLane > 1) secondLane = -1;
            SpawnSingleObstacle(secondLane);
        }
    }

    private void SpawnSingleObstacle(int lane)
    {
        bool isHigh = _rng.NextDouble() < 0.45;
        float height = isHigh ? 2.4f : 0.75f;
        float width = LaneWidth * 0.78f;
        float depth = 0.95f;

        SceneObject obj = AddObject(isHigh ? "obstacol înalt" : "obstacol jos", _cubeMesh);
        obj.Tint = isHigh
            ? new Color4(0.90f, 0.24f, 0.20f, 1.0f)
            : new Color4(0.95f, 0.72f, 0.18f, 1.0f);

        float x = lane * LaneWidth;
        float centerY = height * 0.5f;
        obj.Transform.Position = new Vector3(x, centerY, SpawnZ);
        obj.Transform.Scale = new Vector3(width, height, depth);

        _obstacles.Add(new Obstacle
        {
            Obj = obj,
            Lane = lane,
            IsHigh = isHigh,
            Z = SpawnZ,
            CenterY = centerY,
            HalfExtents = new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f)
        });
    }

    private static bool OverlapsAABB(
        Vector3 centerA, Vector3 halfA,
        Vector3 centerB, Vector3 halfB)
    {
        bool overlapX = MathF.Abs(centerA.X - centerB.X) < (halfA.X + halfB.X);
        bool overlapY = MathF.Abs(centerA.Y - centerB.Y) < (halfA.Y + halfB.Y);
        bool overlapZ = MathF.Abs(centerA.Z - centerB.Z) < (halfA.Z + halfB.Z);
        return overlapX && overlapY && overlapZ;
    }

    public override void Update(float dt, Input input)
    {
        if (input.WasPressed(Restart))
        {
            ResetGame();
            ConsolePanel.PrintMessage("joc nou pornit");
            return;
        }

        if (_gameOver) return;

        // Schimbare bandă
        if (input.WasPressed(LaneLeft)) SwitchLane(-1);
        if (input.WasPressed(LaneRight)) SwitchLane(+1);

        // Salt
        if (input.WasPressed(Jump) && _isGrounded)
        {
            _velocityY = JumpVelocity;
            _isGrounded = false;
        }

        // Interpolare orizontală cu Tween
        if (_laneTween != null)
        {
            _laneTween.Update(dt);
            _playerX = _laneTween.Value.X;
            if (_laneTween.IsFinished) _laneTween = null;
        }

        // Mișcare verticală cu gravitație
        if (!_isGrounded)
        {
            _velocityY -= Gravity * dt;
            _playerY += _velocityY * dt;
            if (_playerY <= 0.0f)
            {
                _playerY = 0.0f;
                _velocityY = 0.0f;
                _isGrounded = true;
            }
        }

        _playerRoot.Transform.Position = new Vector3(_playerX, _playerY, 0.0f);

        // Actualizare umbră sub jucător
        float shadowScale = 0.95f / (1.0f + 0.25f * _playerY);
        _shadow.Transform.Position = new Vector3(_playerX, 0.01f, 0.0f);
        _shadow.Transform.Scale = new Vector3(shadowScale, 1.0f, shadowScale);

        // Animație alergare (brațe și picioare) + ușoară înclinare la schimbarea benzii
        _runCycle += dt * (_worldSpeed * 0.85f);
        float swing = _isGrounded ? MathF.Sin(_runCycle) * 32.0f : 20.0f;
        _leftLeg.Transform.Rotation = new Vector3(swing, 0.0f, 0.0f);
        _rightLeg.Transform.Rotation = new Vector3(-swing, 0.0f, 0.0f);
        _leftArm.Transform.Rotation = new Vector3(-swing * 0.8f, 0.0f, 0.0f);
        _rightArm.Transform.Rotation = new Vector3(swing * 0.8f, 0.0f, 0.0f);

        float targetX = _currentLane * LaneWidth;
        _playerRoot.Transform.Rotation = new Vector3(0.0f, 0.0f, (_playerX - targetX) * 8.0f);

        // Scor și creștere treptată a vitezei
        _score += dt;
        if (_score > _bestScore) _bestScore = _score;

        _worldSpeed = MathF.Min(34.0f, 14.0f + _score * 0.45f);
        _spawnInterval = MathF.Max(0.48f, 1.25f - _score * 0.018f);

        // Deplasare marcaje rutiere
        const float roadSpan = DespawnZ - SpawnZ;
        foreach (RoadStripe stripe in _stripes)
        {
            stripe.Z += _worldSpeed * dt;
            if (stripe.Z > DespawnZ)
                stripe.Z -= roadSpan;
            Vector3 pos = stripe.Obj.Transform.Position;
            stripe.Obj.Transform.Position = new Vector3(pos.X, pos.Y, stripe.Z);
        }

        // Generare valuri de obstacole
        _spawnTimer -= dt;
        if (_spawnTimer <= 0.0f)
        {
            SpawnObstacleWave();
            _spawnTimer = _spawnInterval;
        }

        // Mișcare obstacole și coliziune AABB
        Vector3 playerCenter = new(_playerX, _playerY + PlayerHalfH, 0.0f);
        Vector3 playerHalf = new(PlayerHalfW, PlayerHalfH, PlayerHalfD);

        for (int i = _obstacles.Count - 1; i >= 0; i--)
        {
            Obstacle obs = _obstacles[i];
            obs.Z += _worldSpeed * dt;
            obs.Obj.Transform.Position = new Vector3(obs.Obj.Transform.Position.X, obs.CenterY, obs.Z);

            Vector3 obsCenter = new(obs.Obj.Transform.Position.X, obs.CenterY, obs.Z);
            if (OverlapsAABB(playerCenter, playerHalf, obsCenter, obs.HalfExtents))
            {
                _gameOver = true;
                _playerBody.Tint = new Color4(0.95f, 0.20f, 0.20f, 1.0f);
                ConsolePanel.PrintMessage($"Coliziune! Scor final: {_score:F1} s (Record: {_bestScore:F1} s)");
                break;
            }

            if (obs.Z > DespawnZ)
            {
                Objects.Remove(obs.Obj);
                _obstacles.RemoveAt(i);
            }
        }
    }

    public override IEnumerable<string> Describe()
    {
        foreach (string line in base.Describe()) yield return line;
        string banda = _currentLane switch { -1 => "stânga", 0 => "centru", _ => "dreapta" };
        yield return $"Varianta B     : stare = {(_gameOver ? "Game Over" : "în joc")}   bandă = {banda}   y = {_playerY:F2} m";
        yield return $"Dinamică       : viteză = {_worldSpeed:F1} m/s   obstacole = {_obstacles.Count}   scor = {_score:F1} s (record {_bestScore:F1} s)";
    }
}
