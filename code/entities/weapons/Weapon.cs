﻿using Sandbox;
using System;
using System.Collections.Generic;

namespace Facepunch.CoreWars
{
	public abstract partial class Weapon : BaseWeapon
	{
		public abstract WeaponConfig Config { get; }
		public virtual string MuzzleAttachment => "muzzle";
		public virtual string MuzzleFlashEffect => "particles/pistol_muzzleflash.vpcf";
		public virtual string CrosshairClass => "automatic";
		public virtual string ImpactEffect => null;
		public virtual int ClipSize => 16;
		public virtual float AutoReloadDelay => 1.5f;
		public virtual float ReloadTime => 3.0f;
		public virtual bool IsMelee => false;
		public virtual float DamageFalloffStart => 0f;
		public virtual float DamageFalloffEnd => 0f;
		public virtual float BulletRange => 20000f;
		public virtual bool AutoReload => true;
		public virtual string TracerEffect => null;
		public virtual bool ReloadAnimation => true;
		public virtual bool UnlimitedAmmo => false;
		public virtual bool IsPassive => false;
		public virtual float MeleeDuration => 0.4f;
		public virtual float MeleeDamage => 80f;
		public virtual float MeleeForce => 2f;
		public virtual float MeleeRange => 200f;
		public virtual float MeleeRate => 1f;
		public virtual float ChargeAttackDuration => 2f;
		public virtual DamageFlags DamageType => DamageFlags.Bullet;
		public virtual int HoldType => 1;
		public virtual int ViewModelMaterialGroup => 0;
		public override string ViewModelPath => "weapons/rust_pistol/v_rust_pistol.vmdl";

		[Net]
		public int Slot { get; set; }

		[Net, Predicted]
		public int AmmoClip { get; set; }

		[Net, Predicted]
		public TimeSince TimeSinceReload { get; set; }

		[Net, Predicted]
		public bool IsReloading { get; set; }

		[Net, Predicted]
		public TimeSince TimeSinceDeployed { get; set; }

		[Net, Predicted]
		public TimeSince TimeSinceChargeAttack { get; set; }

		[Net, Predicted]
		public TimeSince TimeSinceMeleeAttack { get; set; }

		public float ChargeAttackEndTime { get; private set; }
		public AnimatedEntity AnimationOwner => Owner as AnimatedEntity;

		public int AvailableAmmo()
		{
			if ( Owner is not Player owner ) return 0;
			return owner.GetAmmoCount( Config.AmmoType );
		}

		public float GetDamageFalloff( float distance, float damage )
		{
			if ( DamageFalloffEnd > 0f )
			{
				if ( DamageFalloffStart > 0f )
				{
					if ( distance < DamageFalloffStart )
					{
						return damage;
					}

					var falloffRange = DamageFalloffEnd - DamageFalloffStart;
					var difference = (distance - DamageFalloffStart);

					return Math.Max( damage - (damage / falloffRange) * difference, 0f );
				}

				return Math.Max( damage - (damage / DamageFalloffEnd) * distance, 0f );
			}

			return damage;
		}

		public virtual bool IsAvailable()
		{
			return true;
		}

		public virtual void PlayAttackAnimation()
		{
			AnimationOwner?.SetAnimParameter( "b_attack", true );
		}

		public virtual void PlayReloadAnimation()
		{
			AnimationOwner?.SetAnimParameter( "b_reload", true );
		}

		public override bool CanReload()
		{
			if ( AutoReload && TimeSincePrimaryAttack > AutoReloadDelay && AmmoClip == 0 )
			{
				return true;
			}

			return base.CanReload();
		}

		public override void ActiveStart( Entity owner )
		{
			base.ActiveStart( owner );

			PlaySound( $"weapon.pickup{Rand.Int( 1, 4 )}" );

			TimeSinceDeployed = 0f;
		}

		public override void Spawn()
		{
			base.Spawn();

			AmmoClip = ClipSize;

			SetModel( "weapons/rust_pistol/rust_pistol.vmdl" );
		}

		public override void Reload()
		{
			if ( IsMelee || IsReloading )
				return;

			if ( AmmoClip >= ClipSize )
				return;

			TimeSinceReload = 0f;

			if ( Owner is Player player )
			{
				if ( !UnlimitedAmmo )
				{
					if ( player.GetAmmoCount( Config.AmmoType ) <= 0 )
						return;
				}
			}

			IsReloading = true;

			if ( ReloadAnimation )
				PlayReloadAnimation();

			PlayReloadSound();
			DoClientReload();
		}

		public override void Simulate( Client owner )
		{
			if ( owner.Pawn is Player player )
			{
				if ( owner.Pawn.LifeState == LifeState.Alive )
				{
					if ( ChargeAttackEndTime > 0f && Time.Now >= ChargeAttackEndTime )
					{
						OnChargeAttackFinish();
						ChargeAttackEndTime = 0f;
					}
				}
				else
				{
					ChargeAttackEndTime = 0f;
				}
			}

			if ( !IsReloading )
			{
				base.Simulate( owner );
			}

			if ( IsReloading && TimeSinceReload > ReloadTime )
			{
				OnReloadFinish();
			}
		}

		public override bool CanPrimaryAttack()
		{
			if ( ChargeAttackEndTime > 0f && Time.Now < ChargeAttackEndTime )
				return false;

			if ( TimeSinceMeleeAttack < MeleeDuration )
				return false;

			if ( TimeSinceDeployed < 0.3f )
				return false;

			return base.CanPrimaryAttack();
		}

		public override bool CanSecondaryAttack()
		{
			if ( ChargeAttackEndTime > 0f && Time.Now < ChargeAttackEndTime )
				return false;

			return base.CanSecondaryAttack();
		}

		public virtual void StartChargeAttack()
		{
			ChargeAttackEndTime = Time.Now + ChargeAttackDuration;
		}

		public virtual void OnChargeAttackFinish() { }

		public virtual void OnReloadFinish()
		{
			IsReloading = false;

			if ( Owner is Player player )
			{
				if ( !UnlimitedAmmo )
				{
					var ammo = player.TakeAmmo( Config.AmmoType, (ushort)(ClipSize - AmmoClip) );

					if ( ammo == 0 )
						return;

					AmmoClip += ammo;
				}
				else
				{
					AmmoClip = ClipSize;
				}
			}
		}

		public virtual void PlayReloadSound()
		{

		}

		[ClientRpc]
		public virtual void DoClientReload()
		{
			if ( ReloadAnimation )
			{
				ViewModelEntity?.SetAnimParameter( "reload", true );
			}
		}

		public override void AttackPrimary()
		{
			TimeSincePrimaryAttack = 0;
			TimeSinceSecondaryAttack = 0;

			Rand.SetSeed( Time.Tick );

			ShootEffects();
			ShootBullet( 0.05f, 1.5f, Config.Damage, 3.0f );
		}

		public virtual void MeleeStrike( float damage, float force )
		{
			var forward = Owner.EyeRotation.Forward;
			forward = forward.Normal;

			foreach ( var trace in TraceBullet( Owner.EyePosition, Owner.EyePosition + forward * MeleeRange, 10f ) )
			{
				if ( !trace.Entity.IsValid() )
					continue;

				if ( !IsValidMeleeTarget( trace.Entity ) )
					continue;

				if ( IsServer )
				{
					using ( Prediction.Off() )
					{
						var damageInfo = new DamageInfo()
							.WithPosition( trace.EndPosition )
							.WithFlag( DamageFlags.Blunt )
							.WithForce( forward * 100f * force )
							.UsingTraceResult( trace )
							.WithAttacker( Owner )
							.WithWeapon( this );

						damageInfo.Damage = damage;

						trace.Entity.TakeDamage( damageInfo );
					}
				}
			}
		}

		public virtual void ShootBullet( float spread, float force, float damage, float bulletSize )
		{
			var forward = Owner.EyeRotation.Forward;
			forward += (Vector3.Random + Vector3.Random + Vector3.Random + Vector3.Random) * spread * 0.25f;
			forward = forward.Normal;

			foreach ( var trace in TraceBullet( Owner.EyePosition, Owner.EyePosition + forward * BulletRange, bulletSize ) )
			{
				if ( string.IsNullOrEmpty( ImpactEffect ) )
				{
					trace.Surface.DoBulletImpact( trace );
				}

				var fullEndPos = trace.EndPosition + trace.Direction * bulletSize;

				if ( !string.IsNullOrEmpty( TracerEffect ) )
				{
					var tracer = Particles.Create( TracerEffect, GetEffectEntity(), MuzzleAttachment );
					tracer?.SetPosition( 1, fullEndPos );
					tracer?.SetPosition( 2, trace.Distance );
				}

				if ( !string.IsNullOrEmpty( ImpactEffect ) )
				{
					var impact = Particles.Create( ImpactEffect, fullEndPos );
					impact?.SetForward( 0, trace.Normal );
				}

				if ( !IsServer )
					continue;

				if ( trace.Entity.IsValid() )
				{
					using ( Prediction.Off() )
					{
						var damageInfo = new DamageInfo()
							.WithPosition( trace.EndPosition )
							.WithFlag( DamageType )
							.WithForce( forward * 100f * force )
							.UsingTraceResult( trace )
							.WithAttacker( Owner )
							.WithWeapon( this );

						damageInfo.Damage = GetDamageFalloff( trace.Distance, damage );

						trace.Entity.TakeDamage( damageInfo );
					}
				}
			}
		}

		public bool TakeAmmo( int amount )
		{
			if ( AmmoClip < amount )
				return false;

			AmmoClip -= amount;
			return true;
		}

		public override void CreateViewModel()
		{
			Host.AssertClient();

			if ( string.IsNullOrEmpty( ViewModelPath ) )
				return;

			ViewModelEntity = new ViewModel
			{
				EnableViewmodelRendering = true,
				Position = Position,
				Owner = Owner
			};

			ViewModelEntity.SetModel( ViewModelPath );
			ViewModelEntity.SetMaterialGroup( ViewModelMaterialGroup );
		}

		public override void CreateHudElements()
		{
			if ( Local.Hud == null ) return;
		}

		public bool IsUsable()
		{
			if ( IsMelee || ClipSize == 0 || AmmoClip > 0 )
			{
				return true;
			}

			return AvailableAmmo() > 0;
		}

		public override IEnumerable<TraceResult> TraceBullet( Vector3 start, Vector3 end, float radius = 2.0f )
		{
			bool inWater = Map.Physics.IsPointWater( start );

			yield return Trace.Ray( start, end )
				.UseHitboxes()
				.HitLayer( CollisionLayer.Water, !inWater )
				.Ignore( Owner )
				.Ignore( this )
				.Size( radius )
				.Run();
		}

		protected virtual void CreateMuzzleFlash()
		{
			if ( !string.IsNullOrEmpty( MuzzleFlashEffect ) )
			{
				Particles.Create( MuzzleFlashEffect, GetEffectEntity(), "muzzle" );
			}
		}

		[ClientRpc]
		protected virtual void ShootEffects()
		{
			Host.AssertClient();

			if ( !IsMelee )
			{
				CreateMuzzleFlash();
			}

			ViewModelEntity?.SetAnimParameter( "fire", true );
		}

		protected virtual ModelEntity GetEffectEntity()
		{
			return EffectEntity;
		}

		protected virtual bool IsValidMeleeTarget( Entity target )
		{
			return target is Player;
		}

		protected void DealDamage( Entity target, Vector3 position, Vector3 force )
		{
			DealDamage( target, position, force, Config.Damage );
		}

		protected void DealDamage( Entity target, Vector3 position, Vector3 force, float damage )
		{
			var damageInfo = new DamageInfo()
				.WithAttacker( Owner )
				.WithWeapon( this )
				.WithPosition( position )
				.WithForce( force )
				.WithFlag( DamageType );

			damageInfo.Damage = damage;

			target.TakeDamage( damageInfo );
		}
	}
}
