using Sandbox.Physics;

public partial class PhysGun : BaseWeapon, IPlayerEvent
{
	[Property] public float MinTargetDistance { get; set; } = 0.0f;
	[Property] public float MaxTargetDistance { get; set; } = 10000.0f;
	[Property] public float LinearFrequency { get; set; } = 20.0f;
	[Property] public float LinearDampingRatio { get; set; } = 1.0f;
	[Property] public float AngularFrequency { get; set; } = 20.0f;
	[Property] public float AngularDampingRatio { get; set; } = 1.0f;
	[Property] public float TargetDistanceSpeed { get; set; } = 25.0f;
	[Property] public float RotateSpeed { get; set; } = 0.125f;
	[Property] public float RotateSnapAt { get; set; } = 45.0f;

	[Sync] public Vector3 HoldPos { get; set; }
	[Sync] public Rotation HoldRot { get; set; }
	[Sync] public GameObject GrabbedObject { get; set; }
	[Sync] public Vector3 GrabbedPos { get; set; }
	[Sync] public int GrabbedBone { get; set; } = -1;

	GameObject lastGrabbed = null;
	PhysicsBody _heldBody;

	PhysicsBody HeldBody
	{
		get
		{
			if ( GrabbedObject != lastGrabbed && GrabbedObject != null )
			{
				if ( GrabbedBone > -1 )
				{
					ModelPhysics modelPhysics = GrabbedObject.Components.Get<ModelPhysics>();
					_heldBody = modelPhysics.PhysicsGroup.GetBody( GrabbedBone );
				}
				else
				{
					Rigidbody rigidbody = GrabbedObject.Components.Get<Rigidbody>();
					_heldBody = rigidbody.PhysicsBody;
				}
			}

			lastGrabbed = GrabbedObject;
			return _heldBody;
		}
	}

	float HoldDistance;

	Vector3 HeldPos;
	Rotation HeldRot;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		GrabbedObject = null;
	}

	protected override void OnPreRender()
	{
		UpdateEffects();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !GrabbedObject.IsValid() )
			return;

		if ( GrabbedObject.IsProxy )
			return;

		var velocity = HeldBody.Velocity;
		Vector3.SmoothDamp( HeldBody.Position, HoldPos, ref velocity, 0.075f, Time.Delta );
		HeldBody.Velocity = velocity;

		var angularVelocity = HeldBody.AngularVelocity;
		Rotation.SmoothDamp( HeldBody.Rotation, HoldRot, ref angularVelocity, 0.075f, Time.Delta );
		HeldBody.AngularVelocity = angularVelocity;
	}
	bool grabbed;
	public override void OnControl()
	{
		var eyeRot = Rotation.From( new Angles( 0.0f, Owner.Controller.EyeAngles.yaw, 0.0f ) );

		Owner.Controller.UseCameraControls = !Input.Down( "use" ) || !GrabbedObject.IsValid();

		base.OnControl();

		if ( !GrabbedObject.IsValid() && Input.Down( "attack1" ) && !grabbed)
		{
			TryStartGrab();
			grabbed = true;
		}

		if ( Input.Released( "attack1" ) )
		{
			TryEndGrab();
			grabbed = false;
		}
			

		if(Input.Pressed("reload") && Input.Down("run"))
			TryUnfreezeAll();

		if ( !GrabbedObject.IsValid() )
			return;

		if ( Input.Pressed( "attack2" ) )
		{
			Freeze();

			if ( GrabbedObject.IsValid() )
			{
			}

			GrabbedObject = null;
			return;
		}

		MoveTargetDistance( Input.MouseWheel.y * TargetDistanceSpeed );

		if ( Input.Down( "use" ) )
			DoRotate( eyeRot, Input.MouseDelta * RotateSpeed );

		HoldPos = Owner.AimRay.Position - HeldPos * HeldBody.Rotation + Owner.AimRay.Forward * HoldDistance;
		HoldRot = Owner.Controller.EyeAngles * HeldRot;

		if ( Input.Down( "run" ) && Input.Down( "use" ) )
		{
			var angles = HoldRot.Angles();

			HoldRot = Rotation.From(
				MathF.Round( angles.pitch / RotateSnapAt ) * RotateSnapAt,
				MathF.Round( angles.yaw / RotateSnapAt ) * RotateSnapAt,
				MathF.Round( angles.roll / RotateSnapAt ) * RotateSnapAt
			);
		}
	}

	[Broadcast]
	private void TryUnfreezeAll( )
	{
		
		var rootEnt = GrabbedObject;
		if(!GrabbedObject.IsValid())
		{
			var tr = Scene.Trace.Ray( Owner.AimRay, MaxTargetDistance )
			.UseHitboxes()
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

			if ( !tr.Hit || !tr.GameObject.IsValid() || tr.Component is MapCollider ) return;
			rootEnt = tr.GameObject.Root;
		}

		if ( !rootEnt.IsValid() ) return;

		if(rootEnt.IsProxy)
			return;

		var weldContexts = GetAllConnectedProps( rootEnt );
		bool unfrozen = false;

		


		for ( int i = 0; i < weldContexts.Count; i++ )
		{
			var body = weldContexts[i].Components.Get<Rigidbody>().PhysicsBody;
			if ( !body.IsValid() ) continue;

			if(body.PhysicsGroup.IsValid())
			{
				foreach(var b in body.PhysicsGroup.Bodies)
				{
					if ( b.BodyType == PhysicsBodyType.Static )
					{
						b.BodyType = PhysicsBodyType.Dynamic;
						unfrozen = true;
					}
				}
			}
			else
			{
				if ( body.BodyType == PhysicsBodyType.Static )
				{
					body.BodyType = PhysicsBodyType.Dynamic;
					unfrozen = true;	
				}
			}
		}
		
		
		if ( unfrozen )
		{
			// var freezeEffect = Particles.Create( "particles/physgun_freeze.vpcf" );
			// freezeEffect.SetPosition( 0, tr.EndPosition );
		}

		
	}

	public static List<GameObject> GetAllConnectedProps( GameObject gameObject )
	{
		PropHelper propHelper = gameObject.Components.Get<PropHelper>();

		if ( !propHelper.IsValid() )
			return null;

		var result = new List<PhysicsJoint>();
		var visited = new HashSet<PropHelper>();

		CollectWelds( propHelper, result, visited );

		List<GameObject> returned = new();

		foreach ( PhysicsJoint joint in result )
		{
			returned.Add( joint.Body1.GetGameObject() );
			returned.Add( joint.Body2.GetGameObject() );
		}

		return returned;
	}

	private static void CollectWelds( PropHelper propHelper, List<PhysicsJoint> result, HashSet<PropHelper> visited )
	{
		if ( visited.Contains( propHelper ) )
			return;

		visited.Add( propHelper );
		result.AddRange( propHelper.Joints );

		foreach ( var joint in propHelper.Joints )
		{
			GameObject jointObject = joint.Body1.GetGameObject();

			if ( jointObject == propHelper.GameObject )
			{
				jointObject = joint.Body2.GetGameObject();
			}

			if ( !jointObject.IsValid() )
				return;

			PropHelper propHelper1 = jointObject.Components.Get<PropHelper>();

			if ( !propHelper1.IsValid() )
				return;

			CollectWelds( propHelper1, result, visited );
		}
	}

	private void TryStartGrab()
	{
		var tr = Scene.Trace.Ray( Owner.AimRay, 1024f )
			.UseHitboxes()
			.WithAnyTags( "solid", "player", "debris", "nocollide" )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( !tr.Hit || !tr.GameObject.IsValid() || tr.Component is MapCollider || tr.StartedSolid || tr.Tags.Contains( "map" ) || tr.Tags.Contains( "grabbed" ) ) return;

		var rootEnt = tr.GameObject;
		GrabbedObject = rootEnt;

		bool isRagdoll = GrabbedObject.Components.Get<ModelPhysics>().IsValid();
		GrabbedBone = isRagdoll ? tr.Body.GroupIndex : -1;

		HoldDistance = Vector3.DistanceBetween( Owner.AimRay.Position, tr.EndPosition );
		HoldDistance = HoldDistance.Clamp( MinTargetDistance, MaxTargetDistance );

		HeldRot = Owner.Controller.EyeAngles.ToRotation().Inverse * HeldBody.Rotation;
		HeldPos = HeldBody.Transform.PointToLocal( tr.EndPosition );

		HoldPos = HeldBody.Position;
		HoldRot = HeldBody.Rotation;

		GrabbedPos = tr.Body.Transform.PointToLocal( tr.EndPosition );

		
		UnFreeze();
	}

	[Broadcast]
	private void TryEndGrab()
	{
		GrabbedObject = null;
		lastGrabbed = null;
	}

	private void MoveTargetDistance( float distance )
	{
		HoldDistance += distance;
		HoldDistance = HoldDistance.Clamp( MinTargetDistance, MaxTargetDistance );
	}

	public void DoRotate( Rotation eye, Vector3 input )
	{
		var localRot = eye;
		localRot *= Rotation.FromAxis( Vector3.Up, input.x * RotateSpeed );
		localRot *= Rotation.FromAxis( Vector3.Right, input.y * RotateSpeed );
		localRot = eye.Inverse * localRot;

		HeldRot = localRot * HeldRot;
	}

	[Broadcast]
	public void Freeze()
	{
		HeldBody.BodyType = PhysicsBodyType.Static;
	}

	[Broadcast]
	public void UnFreeze()
	{
		HeldBody.BodyType = PhysicsBodyType.Dynamic;
	}
}
