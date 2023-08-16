using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ToastieRepublic.Characters.Actions;
using ToastieRepublic.Characters.Modules;
using ToastieRepublic.Game;
using ToastieRepublic.Game.Vehicles;
using ToastieRepublic.Sessions;

namespace ToastieRepublic.Characters.Player
{
	[RequireComponent(typeof(PlayerCharacter))]
	public class PlayerController : MonoBehaviour
	{
		public const float kFastFall = -35.0f;
		public const float kJumpLeeway = 0.15f;
		
		[field: Header("Movement Kit")]
		[field: SerializeField] public IAction DoubleJumpAction { get; private set; }
		[field: SerializeField] public float DoubleJumpPercent { get; private set; }
		[field: SerializeField] public bool ExitingVehicleRestoresDoubleJump { get; private set; }
		[field: SerializeField] public bool HitstunRemovesDoubleJump { get; private set; } = true;

		[field: Header("Defensive Kit")]
		[field: SerializeField] public float QuickRecoverJumpPercent { get; private set; } = .15f;
		[field: SerializeField] public float QuickRecoverSpeedPercent { get; private set; } = .5f;

		[field: NonSerialized] public PlayerCharacter Character { get; private set; } = null;

		private GroundedLocomotionModule m_Locomotion;
		private FlippableModule m_Flippable;
		private LedgeModule m_LedgeModule;
		private ZiplineModule m_ZiplineModule;
		private HookModule m_HookModule;

		private List<IInteractable> m_Interactables = new List<IInteractable>();
		private IInteractable m_ClosestInteractable = null;

		private bool m_CanDoubleJump = false;
		private InputActionPhase m_JumpInputPhase = InputActionPhase.Waiting;

		private void Awake()
		{
			Character = GetComponent<PlayerCharacter>();
		}

		private void OnEnable()
		{
			Character.IsHit += OnIsHit;
			Character.VehicleEnter += OnVehicleEnter;
			Character.VehicleExit += OnVehicleExit;

			EngaugeInput.Player.Jump.started += OnJumpStarted;
			EngaugeInput.Player.Jump.canceled += OnJumpCanceled;
			EngaugeInput.Player.PrimaryAbility.started += OnPrimaryStarted;
			EngaugeInput.Player.PrimaryAbility.canceled += OnPrimaryCanceled;
			EngaugeInput.Player.SecondaryAbility.started += OnSecondaryStarted;
			EngaugeInput.Player.SecondaryAbility.canceled += OnSecondaryCanceled;
			EngaugeInput.Player.SpecialAbility.started += OnSpecialStarted;
			EngaugeInput.Player.SpecialAbility.canceled += OnSpecialCanceled;
			EngaugeInput.Player.Interact.started += OnInteractStarted;
			EngaugeInput.Player.Interact.canceled += OnInteractCanceled;

			if (Character.QInitialized())
			{
				m_LedgeModule.SetShouldGetOnLedgeCallback(ShouldGetOnLedge);
				m_ZiplineModule.SetShouldJumpOff(ShouldJumpOffZipline);
				m_HookModule.SetShouldJumpOffHook(ShouldJumpOffHook);
			}

			m_CanDoubleJump = false;
			m_JumpInputPhase = InputActionPhase.Waiting;
		}
		
		private void Start()
		{
			m_Locomotion = Character.FindModule<GroundedLocomotionModule>();

			m_Flippable = Character.FindModule<FlippableModule>();

			m_LedgeModule = Character.FindModule<LedgeModule>();
			m_LedgeModule.SetShouldGetOnLedgeCallback(ShouldGetOnLedge);

			m_ZiplineModule = Character.FindModule<ZiplineModule>();
			m_ZiplineModule.SetShouldJumpOff(ShouldJumpOffZipline);

			m_HookModule = Character.FindModule<HookModule>();
			m_HookModule.SetShouldJumpOffHook(ShouldJumpOffHook);
		}

		private void OnDisable()
		{
			Character.IsHit -= OnIsHit;
			Character.VehicleEnter += OnVehicleEnter;
			Character.VehicleExit -= OnVehicleExit;

			EngaugeInput.Player.Jump.started -= OnJumpStarted;
			EngaugeInput.Player.Jump.canceled -= OnJumpCanceled;
			EngaugeInput.Player.PrimaryAbility.started -= OnPrimaryStarted;
			EngaugeInput.Player.PrimaryAbility.canceled -= OnPrimaryCanceled;
			EngaugeInput.Player.SecondaryAbility.started -= OnSecondaryStarted;
			EngaugeInput.Player.SecondaryAbility.canceled -= OnSecondaryCanceled;
			EngaugeInput.Player.SpecialAbility.started -= OnSpecialStarted;
			EngaugeInput.Player.SpecialAbility.canceled -= OnSpecialCanceled;
			EngaugeInput.Player.Interact.started -= OnInteractStarted;
			EngaugeInput.Player.Interact.canceled -= OnInteractCanceled;

			if (m_LedgeModule != null)
			{
				m_LedgeModule.SetShouldGetOnLedgeCallback(null);
			}
			if (m_ZiplineModule != null)
			{
				m_ZiplineModule.SetShouldJumpOff(null);
			}
			if (m_HookModule != null)
			{
				m_HookModule.SetShouldJumpOffHook(null);
			}
		}

		private void Update()
		{
			if (EngaugeInput.Player.enabled)
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				// Allow player to self-destruct in development build
				// to avoid soft locks
				if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
				{
					Character.Kill(CauseOfDeath.SelfDestruct);
				}
#endif

				var input = EngaugeInput.Player.Move.ReadValue<Vector2>();
				Character.SetInputDir(input);

				// Double Jump
				m_CanDoubleJump |= m_Locomotion.Grounded;

				if (Character.IsInFree())
				{
					// Short Hop
					if (m_JumpInputPhase == InputActionPhase.Canceled && m_Locomotion.VelocityGravity.y > 1.0f)
					{
						// Jump has already started, cut it short
						var grav = m_Locomotion.VelocityGravity;
						grav.y *= 0.6f;
						m_Locomotion.SetVelocityGravity(grav);
						m_JumpInputPhase = InputActionPhase.Waiting;
					}

					// Fast Fall
					if (m_Locomotion.VelocityGravity.y < 0.0f && input.y < 0.0f && m_Locomotion.VelocityGravity.y > kFastFall)
					{
						// Accelerate to quick-fall speed
						var grav = m_Locomotion.VelocityGravity;
						grav.y = Mathf.MoveTowards(m_Locomotion.VelocityGravity.y, kFastFall, 100.0f * Time.deltaTime);
						m_Locomotion.SetVelocityGravity(grav);
					}

					if (Mathf.Abs(input.x) > .5f)
					{
						m_Flippable.SetFacing(input.x);
					}
				}
			}

			UpdateInteractables();
		}

		/// <summary> Called when approaching an interactable object </summary>
		/// <param name="aObj"> The interactable object </param>
		public void InteractableApproach(IInteractable aObj)
		{
			if (m_Interactables.Contains(aObj) == false)
			{
				m_Interactables.Add(aObj);
				
				var character = aObj as Character;
				if (character != null)
				{
					character.Death -= OnInteractableDeath;
					character.Death += OnInteractableDeath;
				}
			}
		}
		
		private void OnInteractableDeath(Character aCharacter, CauseOfDeath aCause)
		{
			aCharacter.Death -= OnInteractableDeath;
			InteractableLeave(aCharacter as IInteractable);
		}

		/// <summary> Called when leaving an interactable object </summary>
		/// <param name="aObj"> The interactable object </param>
		public void InteractableLeave(IInteractable aObj)
		{
			aObj.MarkAsInactive();
			m_Interactables.Remove(aObj);
			
			var character = aObj as Character;
			if (character != null)
			{
				character.Death -= OnInteractableDeath;
			}
		}

		/// <summary> Update the state of interactable objects </summary>
		private void UpdateInteractables()
		{
			if (EngaugeInput.Player.enabled && Character.IsInFree() && m_Interactables.Count > 0)
			{
				// Sort based on distance
				var middle = Character.GetMiddlePoint();
				m_Interactables.Sort((aA, aB) =>
				{
					var da = Vector2.Distance(aA.GetMiddlePosition(), middle);
					var db = Vector2.Distance(aB.GetMiddlePosition(), middle);
					return da.CompareTo(db);
				});

				// Reset all interactables as inactive
				m_ClosestInteractable = null;
				foreach (var obj in m_Interactables)
				{
					if (m_ClosestInteractable == null && obj.CanInteractWith())
					{
						m_ClosestInteractable = obj;
						m_ClosestInteractable.MarkAsActive();
					}
					else
					{
						obj.MarkAsInactive();
					}
				}
			}
			else
			{
				m_ClosestInteractable = null;

				// If the player isn't able to interact, mark all as inactive
				foreach (var obj in m_Interactables)
				{
					obj.MarkAsInactive();
				}
			}
		}

		/// <summary> On is hit callback </summary>
		/// <param name="aSender"> Character who was hit </param>
		/// <param name="aArgs"> The hit args </param>
		private void OnIsHit(Character aSender, in CombatPipeline.CombatResultArgs aArgs)
		{
			if (HitstunRemovesDoubleJump && !Character.QCanAct())
			{
				m_CanDoubleJump = false;
			}
		}

		/// <summary> Enter vehicle callback </summary>
		/// <param name="aSender"> Character who entered the vehicle </param>
		/// <param name="aVehicle"> The vehicle that was entered </param>
		private void OnVehicleEnter(Character aSender, IVehicle aVehicle)
			=> m_JumpInputPhase = InputActionPhase.Waiting;

		/// <summary> Exit vehicle callback </summary>
		/// <param name="aSender"> Character who exited the vehicle </param>
		/// <param name="aVehicle"> The vehicle that was exited </param>
		private void OnVehicleExit(Character aSender, IVehicle aVehicle)
			=> m_CanDoubleJump |= ExitingVehicleRestoresDoubleJump;

		/// <summary> Fall-through Platform / Jump / Double Jump </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnJumpStarted(InputAction.CallbackContext aContext)
		{
			if (!Character.IsInFree())
				return;

			if (m_Locomotion.Tumbling)
			{
				m_Locomotion.QuickRecover(QuickRecoverJumpPercent, QuickRecoverSpeedPercent);
				return;
			}

			var input = Character.QInputDir();
			if (input.y < -0.5f)
			{
				// If holding down, drop through the platform
				m_Locomotion.DropThroughPlatform();
			}
			else
			{
				// Otherwise, try to perform a jump
				if (m_Locomotion.Grounded || m_Locomotion.GetTimeSinceWasGrounded() < kJumpLeeway)
				{
					m_Locomotion.Jump();
					m_JumpInputPhase = InputActionPhase.Started;
				}
				else if (m_CanDoubleJump)
				{
					// Allow player to change direction when double jumping
					// But don't cancel momentum if not changing direction
					if (Mathf.Abs(input.x) > .5f && !Mathf.Approximately(Mathf.Sign(input.x), Mathf.Sign(m_Locomotion.VelocityMovement.x)))
					{
						var move = m_Locomotion.VelocityMovement;
						move.x = input.x * m_Locomotion.QSpeed();
						m_Locomotion.SetVelocityMovement(move);
					}

					m_Locomotion.Jump(DoubleJumpPercent, DoubleJumpAction);

					m_CanDoubleJump = false;
					m_JumpInputPhase = InputActionPhase.Started;
				}
			}
		}

		/// <summary> Short Hop </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnJumpCanceled(InputAction.CallbackContext aContext)
		{
			// Only count as cancelled if an actual jump action was started
			if (m_JumpInputPhase == InputActionPhase.Started)
			{
				m_JumpInputPhase = InputActionPhase.Canceled;
			}
		}
	
		/// <summary> Primary Ability Cast </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnPrimaryStarted(InputAction.CallbackContext aContext)
			=> Character.PrimaryAbilityState?.OnButtonPress();

		/// <summary> Primary Ability Finish Cast </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnPrimaryCanceled(InputAction.CallbackContext aContext)
			=> Character.PrimaryAbilityState?.OnButtonRelease();

		/// <summary> Secondary Ability Cast </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnSecondaryStarted(InputAction.CallbackContext aContext)
			=> Character.SecondaryAbilityState?.OnButtonPress();

		/// <summary> Secondary Ability Finish Cast </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnSecondaryCanceled(InputAction.CallbackContext aContext)
			=> Character.SecondaryAbilityState?.OnButtonRelease();

		/// <summary> Special Ability Cast </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnSpecialStarted(InputAction.CallbackContext aContext)
			=> Character.SpecialAbilityState?.OnButtonPress();

		/// <summary> Special Ability Finish Cast </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnSpecialCanceled(InputAction.CallbackContext aContext)
			=> Character.SpecialAbilityState?.OnButtonRelease();

		/// <summary> Should Get On Ledge Callback </summary>
		/// <param name="aSender"> Character trying to get on the ledge </param>
		private bool ShouldGetOnLedge(Character aSender)
		{
			var input = Character.QInputDir();

			var left = m_Flippable.QFacing() == Direction.Left && input.x < -.5f;
			var right = m_Flippable.QFacing() == Direction.Right && input.x > .5f;
			var up = input.y > .5f;

			return left || right || up;
		}

		/// <summary> Should Jump Off Zipline Callback </summary>
		/// <param name="aSender"> Character trying to jump off the zipline </param>
		private bool ShouldJumpOffZipline(Character aSender)
			=> EngaugeInput.Player.Jump.triggered;

		/// <summary> Should Jump Off Hook Callback </summary>
		/// <param name="aSender"> Character trying to jump off the hook </param>
		private bool ShouldJumpOffHook(Character aSender)
			=> EngaugeInput.Player.Jump.triggered;
		
		/// <summary> Interact Start </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnInteractStarted(InputAction.CallbackContext aContext)
		{
			if (m_Locomotion.Grounded && Character.IsInFree() && m_ClosestInteractable != null)
			{
				m_ClosestInteractable.OnUse(Character);
			}
		}

		/// <summary> Interact Cancel </summary>
		/// <param name="aContext"> Input callback context </param>
		private void OnInteractCanceled(InputAction.CallbackContext aContext)
		{
			if (m_Locomotion.Grounded && Character.IsInFree() && m_ClosestInteractable != null)
			{
				m_ClosestInteractable.OnStopUse(Character);
			}
		}

		/// <summary> Allow small hop to be manually refreshed </summary>
		public void RefreshSmallHop()
			=> m_JumpInputPhase = InputActionPhase.Waiting;

		/// <summary> Allow double jump to be manually refreshed </summary>
		public void RefreshDoubleJump()
			=> m_CanDoubleJump = true;
	}
}