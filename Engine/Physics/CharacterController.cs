using OpenTK.Mathematics;

using ArcEngine.Engine.Core;

namespace ArcEngine.Engine.Physics;

/// <summary>
/// Kinematic capsule-based character controller. Drives the owner's Transform
/// directly (no dynamic Rigidbody — a Rigidbody body would fight the sim's
/// velocity integrator on every kinematic move).
///
/// External code calls <see cref="Move"/> once per frame with the desired
/// horizontal velocity (world XZ); vertical motion is handled internally
/// (gravity, jump, ground snap). The controller uses <see cref="PhysicsWorld"/>'s
/// raycast helper to detect ground and honour slope + step limits.
///
/// v1 limitations:
/// <list type="bullet">
///   <item>Horizontal motion is NOT swept against the world — walls are not
///         solid to the character. Add a shape sweep in a follow-up.</item>
///   <item>Single downward ground ray at the capsule center; use a small tri-cast
///         if you need edge stability.</item>
/// </list>
/// </summary>
public class CharacterController : Component
{
    /// <summary>Capsule radius (also the ground-check ray offset horizontally).</summary>
    public float Radius = 0.4f;

    /// <summary>Total capsule height (radius counts, so height = 2*radius + cylindrical length).</summary>
    public float Height = 1.8f;

    /// <summary>World-space gravity applied while airborne. Positive Y = up.</summary>
    public Vector3 Gravity = new(0f, -9.81f, 0f);

    /// <summary>Maximum walkable slope, in degrees measured from world-up.</summary>
    public float SlopeLimitDegrees = 45f;

    /// <summary>Distance below the capsule considered "still standing on the ground" for snap purposes.</summary>
    public float GroundSnapDistance = 0.15f;

    /// <summary>Vertical velocity that gets set when <see cref="Jump"/> is called and the controller is grounded.</summary>
    public float JumpSpeed = 5f;

    /// <summary>True while the controller registered a ground hit last frame.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>Normal of the ground surface directly under the controller last frame.</summary>
    public Vector3 GroundNormal { get; private set; } = Vector3.UnitY;

    private PhysicsWorld? _world;
    private Vector3 _velocity;           // full velocity (horizontal from Move + vertical from gravity/jump)
    private bool _wantsJump;

    public override void Awake()
    {
        _world = GameObject.Scene?.FindComponent<PhysicsWorld>();
        if (_world == null)
            Console.WriteLine($"[CharacterController] '{GameObject.Name}': no PhysicsWorld in scene — grounding will always fail.");
    }

    /// <summary>Request that a jump be applied on the next physics update, if grounded.</summary>
    public void Jump() => _wantsJump = true;

    /// <summary>
    /// Set the desired HORIZONTAL velocity for this frame. Y component is
    /// ignored — gravity/jump handle vertical motion.
    /// </summary>
    public void Move(Vector3 horizontalVelocity)
    {
        _velocity.X = horizontalVelocity.X;
        _velocity.Z = horizontalVelocity.Z;
    }

    public override void Update(float deltaTime)
    {
        // Ground probe from the capsule center down past its bottom by GroundSnapDistance.
        float centerToFoot = Height * 0.5f;
        var origin = Transform.Position;
        bool hitGround = false;
        Vector3 hitNormal = Vector3.UnitY;
        float hitDistance = float.PositiveInfinity;

        if (_world != null &&
            _world.Raycast(origin, -Vector3.UnitY, centerToFoot + GroundSnapDistance, out var hit))
        {
            hitGround = true;
            hitNormal = hit.Normal.LengthSquared > 0f ? Vector3.Normalize(hit.Normal) : Vector3.UnitY;
            hitDistance = hit.Distance;
        }

        // Slope check — a hit that's too steep counts as air (character slides off).
        float slopeLimitCos = MathF.Cos(MathHelper.DegreesToRadians(SlopeLimitDegrees));
        bool onWalkableSlope = hitGround && hitNormal.Y >= slopeLimitCos;

        IsGrounded = onWalkableSlope;
        GroundNormal = onWalkableSlope ? hitNormal : Vector3.UnitY;

        // Vertical velocity: reset on grounded, accumulate gravity in air.
        if (IsGrounded)
        {
            if (_wantsJump)
            {
                _velocity.Y = JumpSpeed;
                _wantsJump = false;
                IsGrounded = false;
            }
            else if (_velocity.Y < 0f)
            {
                _velocity.Y = 0f;
            }
        }
        else
        {
            _velocity += Gravity * deltaTime;
            _wantsJump = false;   // discard if not grounded — no double-jump v1
        }

        // Integrate.
        Transform.Position += _velocity * deltaTime;

        // Ground snap — if we're within one snap-distance of a walkable surface and
        // NOT jumping upward, glue the feet to it so we don't hop down stairs.
        if (IsGrounded && _velocity.Y <= 0f)
        {
            Transform.Position = new Vector3(
                Transform.Position.X,
                origin.Y - (hitDistance - centerToFoot),
                Transform.Position.Z);
        }

        // Zero horizontal for next frame — caller must call Move() again.
        _velocity.X = 0f;
        _velocity.Z = 0f;
    }
}
