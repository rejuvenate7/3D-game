using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController), typeof(RopeTether))]
[DisallowMultipleComponent]
public class PlayerMovement : MonoBehaviour
{
    public enum MovementMode { Ground, Air, Slide, WallRun, Rail, Grapple }

    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Solid level geometry, rails and hook colliders. Trigger volumes are ignored.")]
    [SerializeField] private LayerMask environmentMask = Physics.DefaultRaycastLayers;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 10f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.8f;
    [SerializeField] private float gravity = -25f;
    [SerializeField, Min(1f)] private float maximumSpeed = 45f;
    [SerializeField, Min(1f)] private float groundAcceleration = 65f;
    [SerializeField, Min(0f)] private float groundFriction = 8f;
    [SerializeField, Min(0f)] private float airAcceleration = 12f;

    [Header("Look")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.1f;
    [SerializeField, Range(1f, 89f)] private float lookLimit = 85f;
    [SerializeField, Range(0f, 20f)] private float wallCameraTilt = 8f;

    [Header("Sliding")]
    [SerializeField, Min(0.8f)] private float slideHeight = 1.05f;
    [SerializeField, Min(1f)] private float slideStartSpeed = 12f;
    [SerializeField, Min(0f)] private float slideGravityMultiplier = 1.65f;
    [SerializeField, Min(0f)] private float slideFriction = 2f;
    [SerializeField, Min(0f)] private float slideSteering = 5f;

    [Header("Wall running")]
    [SerializeField] private LayerMask wallMask = Physics.DefaultRaycastLayers;
    [SerializeField, Min(1f)] private float wallRunSpeed = 18f;
    [SerializeField, Min(0.1f)] private float wallCheckDistance = 0.9f;
    [SerializeField, Min(0.1f)] private float wallRunDuration = 2.5f;
    [SerializeField, Min(0f)] private float wallRunGravity = 4f;
    [SerializeField, Min(0f)] private float wallJumpUpSpeed = 9f;
    [Tooltip("Briefly preserve wall travel direction after exit before allowing air steering.")]
    [SerializeField, Min(0f)] private float wallExitControlDelay = 0.3f;

    [Header("Rail sliding")]
    [SerializeField, Min(1f)] private float railStartSpeed = 16f;
    [SerializeField, Min(1f)] private float railMaximumSpeed = 32f;
    [SerializeField, Min(0f)] private float railAcceleration = 6f;

    [Header("Grapple")]
    [SerializeField, Min(2f)] private float grappleRange = 70f;
    [SerializeField, Min(0f)] private float grappleAimRadius = 2.5f;
    [SerializeField, HideInInspector] private int grappleSettingsVersion;
    [SerializeField, Min(0.5f)] private float minimumRopeLength = 2f;
    [SerializeField, Min(0f)] private float swingAcceleration = 28f;
    [SerializeField, Min(0f)] private float grappleLaunchBoost = 6f;
    [Tooltip("min speed toward the hook immediately on attachment.")]
    [SerializeField, Min(0f)] private float grappleAttachPullSpeed = 12f;
    [Tooltip("target rope length as a fraction of the starting attachment distance.")]
    [SerializeField, Range(0.2f, 1f)] private float grappleLengthMultiplier = 0.65f;
    [Tooltip("auto pull speed until the shorter target length is reached.")]
    [SerializeField, Min(1f)] private float grappleRetractSpeed = 25f;

    [Header("Respawn")]
    [SerializeField] private float fallLimit = -30f;

    public MovementMode Mode { get; private set; } = MovementMode.Air;
    public float Speed => Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
    public bool CanGrapple => aimPoint != null;
    public bool IsGrappling => grapplePoint != null;
    public float RopeLength => ropeLength;

    private CharacterController controller;
    private RopeTether tether;
    private Vector3 velocity;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private float pitch, cameraRoll;
    private Vector3 cameraStartLocal;
    private float standingHeight;
    private Vector3 standingCenter;
    private bool crouched, sliding, grounded;
    private float slideTime, wallTime;
    private float wallCooldown, railCooldown, groundIgnoreTime, wallJumpGrace;
    private Vector3 wallNormal;
    private Vector3 wallTravelDirection;
    private float wallTravelSpeed;
    private float wallExitControlTimer;
    private int wallSide;
    private RailPath rail;
    private float railDistance, railDirection, railSpeed;
    private GrapplePoint aimPoint, grapplePoint;
    private Vector3 previousAnchor;
    private float ropeLength;
    private float targetRopeLength;
    private Vector2 moveInput;
    private bool jumpHeld, jumpPressed, slidePressed, grapplePressed;
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly Collider[] overlaps = new Collider[32];
    private const float SimulationStep = 1f / 120f;

    private Vector3 BodyAttachment => transform.position + Vector3.up * controller.height * 0.65f;
    private Vector3 WishDirection => transform.right * moveInput.x + transform.forward * moveInput.y;

    private void Awake()
    {
        UpgradeGrappleSettings();
        controller = GetComponent<CharacterController>();
        tether = GetComponent<RopeTether>();
        if (tether == null) tether = gameObject.AddComponent<RopeTether>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null || !playerCamera.transform.IsChildOf(transform))
        {
            Debug.LogError("PlayerMovement needs a child Camera assigned to Player Camera.", this);
            enabled = false;
            return;
        }
        standingHeight = controller.height;
        standingCenter = controller.center;
        cameraStartLocal = playerCamera.transform.localPosition;
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        if (transform.lossyScale != Vector3.one)
            Debug.LogWarning("Keep Player and its parents at scale (1,1,1).", this);
    }

    private void Start() { SetCursorLocked(true); }

    private void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.08f);
        if (dt <= 0f) return;
        bool controls = ReadInput();
        aimPoint = controls ? FindGrapplePoint() : null;
        if (!controls && grapplePoint != null) EndGrapple();
        if (controls && grapplePoint != null && jumpPressed)
        {
            EndGrapple();
            jumpPressed = jumpHeld = false;
        }
        else if (controls && grapplePressed)
        {
            if (grapplePoint != null) EndGrapple();
            else if (aimPoint != null) BeginGrapple(aimPoint);
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / SimulationStep));
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            Simulate(step);
            jumpPressed = slidePressed = false;
        }
        if (transform.position.y < fallLimit) Respawn();
        UpdateCamera(dt);
    }

    private bool ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        moveInput = Vector2.zero;
        jumpHeld = jumpPressed = slidePressed = grapplePressed = false;
        bool controls = Application.isFocused && Cursor.lockState == CursorLockMode.Locked;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLocked(false);
            controls = false;
        }
        else if (!controls && Application.isFocused && mouse != null && mouse.leftButton.wasPressedThisFrame)
            SetCursorLocked(true); 
        if (!controls) return false;
        if (mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * mouseSensitivity;
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -lookLimit, lookLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, cameraRoll);
        }
        if (keyboard != null)
        {
            moveInput = Vector2.ClampMagnitude(new Vector2(
                (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f)), 1f);
            jumpHeld = keyboard.spaceKey.isPressed;
            jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
            slidePressed = keyboard.cKey.wasPressedThisFrame;
            grapplePressed = keyboard.eKey.wasPressedThisFrame;
        }
        return true;
    }

    private void Simulate(float dt)
    {
        wallCooldown = Mathf.Max(0f, wallCooldown - dt);
        railCooldown = Mathf.Max(0f, railCooldown - dt);
        groundIgnoreTime = Mathf.Max(0f, groundIgnoreTime - dt);
        wallJumpGrace = Mathf.Max(0f, wallJumpGrace - dt);
        wallExitControlTimer = Mathf.Max(0f, wallExitControlTimer - dt);
        grounded = ProbeGround(out RaycastHit ground) && groundIgnoreTime <= 0f &&
                   Vector3.Dot(velocity, ground.normal) <= 1f;
        if (grounded) { wallTime = 0f; wallJumpGrace = 0f; wallExitControlTimer = 0f; }

        if (Mode == MovementMode.Grapple &&
            (grapplePoint == null || !grapplePoint.isActiveAndEnabled)) EndGrapple();
        if (grapplePoint != null)
        {
            SimulateGrapple(dt);
            return;
        }
        if (rail != null)
        {
            if (!rail.isActiveAndEnabled) ExitRail();
            else { SimulateRail(dt); return; }
        }
        if (railCooldown <= 0f && velocity.y <= 2f && TryEnterRail())
        {
            SimulateRail(dt);
            return;
        }

        if (sliding && (!grounded || slidePressed)) sliding = false;
        else if (slidePressed && grounded)
        {
            sliding = true;
            slideTime = 0f;
            SetCrouched(true);
            Vector3 direction = Vector3.ProjectOnPlane(velocity, ground.normal);
            if (direction.sqrMagnitude < 1f) direction = Vector3.ProjectOnPlane(transform.forward, ground.normal);
            velocity = direction.normalized * Mathf.Max(direction.magnitude, slideStartSpeed);
        }
        if (!sliding && crouched && CanStand()) SetCrouched(false);

        if (jumpPressed && !grounded && wallJumpGrace > 0f)
        {
            ExitWall(true);
            wallJumpGrace = 0f;
            groundIgnoreTime = 0.15f;
        }

        RaycastHit wall = default;
        bool wantsWall = !grounded && !crouched && jumpHeld && wallCooldown <= 0f &&
                         wallTime < wallRunDuration && TryFindWall(out wall);
        if (wantsWall)
        {
            SimulateWall(dt, wall.normal);
            return;
        }
        if (Mode == MovementMode.WallRun) ExitWall(false);

        if (grounded && jumpPressed && (!crouched || CanStand()))
        {
            sliding = false;
            SetCrouched(false);
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
			
            grounded = false;
            groundIgnoreTime = 0.15f;
        }

        if (sliding)
        {
            Mode = MovementMode.Slide;
            slideTime += dt;
            velocity = Vector3.ProjectOnPlane(velocity, ground.normal);
            Vector3 slopeGravity = Vector3.ProjectOnPlane(Vector3.up * gravity, ground.normal);
            velocity += slopeGravity * (slideGravityMultiplier * dt);

            Vector3 steering = Vector3.ProjectOnPlane(WishDirection, ground.normal);
            if (velocity.sqrMagnitude > 0.1f)
                steering = Vector3.ProjectOnPlane(steering, velocity.normalized);
            velocity += steering * (slideSteering * dt);
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, slideFriction * dt);
			
            velocity = Vector3.ClampMagnitude(velocity, maximumSpeed);
            MoveBody(velocity * dt + Vector3.down * (3f * dt));
            if (slideTime > 0.35f && velocity.magnitude < 3f) sliding = false;
            return;
        }

        Mode = grounded ? MovementMode.Ground : MovementMode.Air;
        if (grounded)
        {
            velocity = Vector3.ProjectOnPlane(velocity, ground.normal);

            velocity = Vector3.MoveTowards(velocity, Vector3.zero, groundFriction * dt);
            Vector3 wish = Vector3.ProjectOnPlane(WishDirection, ground.normal).normalized;
            Accelerate(wish, (crouched ? moveSpeed * 0.45f : moveSpeed) * moveInput.magnitude,
                       groundAcceleration, dt);
        }
        else
        {
            if (wallExitControlTimer <= 0f)
                Accelerate(WishDirection.normalized, moveSpeed * moveInput.magnitude, airAcceleration, dt);
            velocity += Vector3.up * gravity * dt;
        }
        velocity = Vector3.ClampMagnitude(velocity, maximumSpeed);
		
        MoveBody(velocity * dt + (grounded ? Vector3.down * (3f * dt) : Vector3.zero));
    }

    private void Accelerate(Vector3 direction, float desiredSpeed, float acceleration, float dt)
    {

        float missing = desiredSpeed - Vector3.Dot(velocity, direction);
        if (missing > 0f) velocity += direction * Mathf.Min(missing, acceleration * dt);
    }

    private bool ProbeGround(out RaycastHit hit)
    {
        float radius = controller.radius * 0.85f;
        Vector3 origin = transform.position + Vector3.up * (radius + 0.12f);
        return Cast(origin, radius, Vector3.down, 0.25f, environmentMask, out hit) &&
               hit.normal.y >= Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);
    }

    private bool TryFindWall(out RaycastHit wall)
    {
        Vector3 origin = transform.position + Vector3.up * (standingHeight * 0.6f);

        if (Mode == MovementMode.WallRun &&
            Cast(origin, 0.15f, -wallNormal, wallCheckDistance, wallMask, out wall) &&
            Mathf.Abs(wall.normal.y) < 0.2f) return true;
        bool left = Cast(origin, 0.15f, -transform.right, wallCheckDistance, wallMask, out RaycastHit l);
        bool right = Cast(origin, 0.15f, transform.right, wallCheckDistance, wallMask, out RaycastHit r);
        left &= Mathf.Abs(l.normal.y) < 0.2f;
        right &= Mathf.Abs(r.normal.y) < 0.2f;
        wall = left && (!right || l.distance < r.distance) ? l : r;
        wallSide = left && (!right || l.distance < r.distance) ? -1 : 1;
        return left || right;
    }

    private void SimulateWall(float dt, Vector3 normal)
    {
        bool entering = Mode != MovementMode.WallRun;
        Mode = MovementMode.WallRun;
        wallTime += dt;
        wallJumpGrace = 0.2f;
        wallNormal = normal;
		
        Vector3 along = Vector3.Cross(normal, Vector3.up).normalized;
        Vector3 reference = velocity.sqrMagnitude > 1f ? velocity : transform.forward;
		
        if (Vector3.Dot(along, reference) < 0f) along = -along;

        float speed = Mathf.Max(wallRunSpeed, Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude);
        float y = entering ? Mathf.Clamp(velocity.y, -1f, 2f) : velocity.y;
		
        y = Mathf.Max(y - wallRunGravity * dt, -3f);
        velocity = along * Mathf.Min(speed, maximumSpeed) + Vector3.up * y;
        wallTravelDirection = along;
        wallTravelSpeed = Mathf.Min(speed, maximumSpeed);
		
        MoveBody(velocity * dt - normal * (4f * dt));
    }

    private void ExitWall(bool jump)
    {
        velocity = wallTravelDirection * wallTravelSpeed + Vector3.up * (jump ? wallJumpUpSpeed : velocity.y);
        wallExitControlTimer = wallExitControlDelay;
        wallCooldown = jump ? 0.25f : 0.15f;
        Mode = MovementMode.Air;
    }

    private bool TryEnterRail()
    {
        RailPath best = null;
        float bestError = float.PositiveInfinity;
        float distance = 0f;
        foreach (RailPath candidate in RailPath.Active)
        {
            if (candidate == null || !candidate.isActiveAndEnabled || candidate.Length < 0.1f) continue;
            if (!candidate.Closest(transform.position, out float along, out Vector3 point)) continue;
            Vector3 offset = transform.position - point;

            if (offset.y < -0.15f || offset.y > candidate.captureRadius || offset.sqrMagnitude >
                candidate.captureRadius * candidate.captureRadius) continue;
            if (offset.sqrMagnitude < bestError)
            {
                best = candidate;
                bestError = offset.sqrMagnitude;
                distance = along;
            }
        }
        if (best == null) return false;
        rail = best;
        railDistance = distance;
        rail.Sample(distance, out _, out Vector3 tangent);
        Vector3 reference = Speed > 1f ? velocity : transform.forward;
        railDirection = Vector3.Dot(reference, tangent) >= 0f ? 1f : -1f;
        railSpeed = Mathf.Clamp(Speed, railStartSpeed, railMaximumSpeed);
        sliding = false;
        if (crouched && CanStand()) SetCrouched(false);
        Mode = MovementMode.Rail;
        return true;
    }

    private void SimulateRail(float dt)
    {
        Mode = MovementMode.Rail;
        rail.Sample(railDistance, out _, out Vector3 tangent);
        tangent *= railDirection;
        if (jumpPressed && (!crouched || CanStand()))
        {
            velocity = tangent * railSpeed + Vector3.up * Mathf.Sqrt(jumpHeight * -2f * gravity);
            ExitRail();
            groundIgnoreTime = 0.15f;
            if (crouched) SetCrouched(false);
            MoveBody(velocity * dt);
            return;
        }
        railSpeed = Mathf.Clamp(railSpeed + (railAcceleration + Vector3.Dot(Vector3.up * gravity, tangent)) * dt,
                                railStartSpeed * 0.7f, railMaximumSpeed);
        float next = railDistance + railDirection * railSpeed * dt;
        float length = rail.Length;
        railDistance = Mathf.Clamp(next, 0f, length);
        rail.Sample(railDistance, out Vector3 target, out tangent);
        velocity = tangent * (railDirection * railSpeed);
        target += Vector3.up * controller.skinWidth;
        MoveBody(target - transform.position);
        if ((transform.position - target).sqrMagnitude > 0.16f || next < 0f || next > length)
            ExitRail();
    }

    private void ExitRail()
    {
        rail = null;
        railCooldown = 0.3f;
        groundIgnoreTime = 0.08f;
        Mode = MovementMode.Air;
    }

    private GrapplePoint FindGrapplePoint()
    {
        GrapplePoint best = null;
        float bestScore = float.PositiveInfinity;
        Vector3 origin = playerCamera.transform.position;
        Vector3 forward = playerCamera.transform.forward;
        foreach (GrapplePoint point in GrapplePoint.Active)
        {
            if (point == null || !point.isActiveAndEnabled ||
                Vector3.Distance(BodyAttachment, point.Position) > grappleRange) continue;
            Vector3 delta = point.Position - origin;
            float along = Vector3.Dot(delta, forward);
            if (along <= 0f) continue;
            float lateralSquared = (delta - forward * along).sqrMagnitude;
            if (lateralSquared > grappleAimRadius * grappleAimRadius) continue;
            float score = lateralSquared / Mathf.Max(delta.sqrMagnitude, 0.001f);
            if (score >= bestScore) continue;
            if (Cast(origin, 0f, delta.normalized, delta.magnitude, environmentMask, out RaycastHit hit) &&
                hit.collider.GetComponentInParent<GrapplePoint>() != point) continue;
            if (RopeBlocked(point)) continue;
            best = point;
            bestScore = score;
        }
        return best;
    }

    private void BeginGrapple(GrapplePoint point)
    {
        Vector3 toAnchor = point.Position - BodyAttachment;
        if (toAnchor.magnitude < minimumRopeLength || RopeBlocked(point)) return;
        grapplePoint = point;
        previousAnchor = point.Position;
        Vector3 tangent = Vector3.ProjectOnPlane(transform.forward, toAnchor.normalized);
        if (tangent.sqrMagnitude > 0.01f) velocity += tangent.normalized * grappleLaunchBoost;
        rail = null;
        railCooldown = 0.3f;
        sliding = false;
        if (crouched && CanStand()) SetCrouched(false);
        ropeLength = Vector3.Distance(BodyAttachment, point.Position);
        targetRopeLength = Mathf.Clamp(ropeLength * grappleLengthMultiplier,
                                      Mathf.Min(minimumRopeLength, ropeLength), ropeLength);
        Vector3 towardHook = (point.Position - BodyAttachment).normalized;
        float towardSpeed = Vector3.Dot(velocity, towardHook);
        if (towardSpeed < grappleAttachPullSpeed)
            velocity += towardHook * (grappleAttachPullSpeed - towardSpeed);
        velocity = Vector3.ClampMagnitude(velocity, maximumSpeed);
        Mode = MovementMode.Grapple;
    }

    private void SimulateGrapple(float dt)
    {
        Mode = MovementMode.Grapple;
        if (RopeBlocked(grapplePoint)) { EndGrapple(); return; }
        Vector3 anchor = grapplePoint.Position;
        Vector3 anchorVelocity = (anchor - previousAnchor) / dt;
        previousAnchor = anchor;
        ropeLength = Mathf.MoveTowards(ropeLength, targetRopeLength, grappleRetractSpeed * dt);
        Vector3 radial = (BodyAttachment - anchor).normalized;
        Vector3 pump = Vector3.ProjectOnPlane(WishDirection, radial);
        velocity += (Vector3.up * gravity + pump * swingAcceleration) * dt;
        velocity = Vector3.ClampMagnitude(velocity, maximumSpeed);
        MoveBody(velocity * dt);

        for (int i = 0; i < 3; i++)
        {
            Vector3 offset = BodyAttachment - anchor;
            float distance = offset.magnitude;
            if (distance <= ropeLength || distance < 0.001f) break;
            MoveBody(-offset * ((distance - ropeLength) / distance));
        }
        Vector3 finalOffset = BodyAttachment - anchor;
        if (finalOffset.magnitude >= ropeLength - 0.03f)
        {
            Vector3 normal = finalOffset.normalized;
            float outward = Vector3.Dot(velocity - anchorVelocity, normal);
            if (outward > 0f) velocity -= normal * outward;
        }
        if (finalOffset.magnitude > ropeLength + 1.5f || RopeBlocked(grapplePoint)) EndGrapple();
    }

    private bool RopeBlocked(GrapplePoint point)
    {
        if (point == null) return true;
        Vector3 start = BodyAttachment;
        Vector3 delta = point.Position - start;
        if (delta.magnitude < 0.1f) return false;
        if (!Cast(start, 0f, delta.normalized, delta.magnitude, environmentMask, out RaycastHit hit)) return false;
        return hit.collider.GetComponentInParent<GrapplePoint>() != point;
    }

    private void EndGrapple()
    {
        grapplePoint = null;
        if (tether != null) tether.Release();
        Mode = MovementMode.Air;
    }

    private void MoveBody(Vector3 displacement)
    {
        CollisionFlags flags = controller.Move(displacement);
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {

        float into = Vector3.Dot(velocity, hit.normal);
        if (into < 0f) velocity -= hit.normal * into;
    }

    private bool Cast(Vector3 origin, float radius, Vector3 direction, float distance,
                      LayerMask mask, out RaycastHit nearest)
    {
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);

        RaycastHit[] results = hits;
        if (count == hits.Length)
        {
            results = radius > 0f
                ? Physics.SphereCastAll(origin, radius, direction, distance, mask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastAll(origin, direction, distance, mask, QueryTriggerInteraction.Ignore);
            count = results.Length;
        }
        nearest = default;
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (results[i].collider.transform.IsChildOf(transform) || results[i].distance >= best) continue;
            nearest = results[i];
            best = nearest.distance;
        }
        return best < float.PositiveInfinity;
    }

    private bool CanStand()
    {
        float radius = Mathf.Max(0.05f, controller.radius - controller.skinWidth);
        Vector3 bottom = transform.position + standingCenter + Vector3.down * (standingHeight * 0.5f - radius);
        Vector3 top = transform.position + standingCenter + Vector3.up * (standingHeight * 0.5f - radius);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlaps, environmentMask,
                                                 QueryTriggerInteraction.Ignore);

        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++)
            if (!overlaps[i].transform.IsChildOf(transform)) return false;
        return true;
    }

    private void SetCrouched(bool value)
    {
        crouched = value;
        float height = value ? Mathf.Clamp(slideHeight, controller.radius * 2f, standingHeight) : standingHeight;
        controller.height = height;
        controller.center = standingCenter + Vector3.down * ((standingHeight - height) * 0.5f);
        controller.stepOffset = Mathf.Min(controller.stepOffset, height * 0.45f);
    }

    private void UpdateCamera(float dt)
    {
        float blend = 1f - Mathf.Exp(-14f * dt);
        float targetRoll = Mode == MovementMode.WallRun ? -wallSide * wallCameraTilt : 0f;
        cameraRoll = Mathf.Lerp(cameraRoll, targetRoll, blend);
        Vector3 target = cameraStartLocal;
        if (crouched) target.y -= standingHeight - controller.height;
        playerCamera.transform.localPosition = Vector3.Lerp(playerCamera.transform.localPosition, target, blend);
        playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, cameraRoll);
    }

    private void LateUpdate()
    {
        if (grapplePoint != null && playerCamera != null)
        {
            Transform cameraTransform = playerCamera.transform;
            Vector3 hand = cameraTransform.position + cameraTransform.right * 0.22f -
                           cameraTransform.up * 0.18f + cameraTransform.forward * 0.35f;
            tether.Draw(hand, grapplePoint.Position, ropeLength, Time.deltaTime);
        }
    }

    public void Respawn()
    {
        EndGrapple();
        rail = null;
        sliding = false;

        controller.enabled = false;
        SetCrouched(false);
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        controller.enabled = true;
        velocity = Vector3.zero;
        pitch = cameraRoll = wallTime = wallCooldown = groundIgnoreTime = wallJumpGrace = 0f;
        wallExitControlTimer = 0f;
        railCooldown = 0.3f;
        playerCamera.transform.localPosition = cameraStartLocal;
        playerCamera.transform.localRotation = Quaternion.identity;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) { EndGrapple(); SetCursorLocked(false); }
    }

    private void OnDisable()
    {
        EndGrapple();
        rail = null;
        sliding = false;
        if (controller != null && crouched && CanStand()) SetCrouched(false);
        SetCursorLocked(false);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnValidate()
    {
        UpgradeGrappleSettings();
        gravity = Mathf.Min(gravity, -0.01f);
        grappleRange = Mathf.Max(grappleRange, minimumRopeLength);
        railMaximumSpeed = Mathf.Max(railMaximumSpeed, railStartSpeed);
        maximumSpeed = Mathf.Max(1f, maximumSpeed);
    }

    private void UpgradeGrappleSettings()
    {

        if (grappleSettingsVersion >= 1) return;
        grappleAimRadius = Mathf.Max(grappleAimRadius, 2.5f);
        grappleSettingsVersion = 1;
    }
}
