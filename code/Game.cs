using Facepunch.CoreWars.Inventory;
using Facepunch.Voxels;
using Sandbox;
using Sandbox.UI;
using System;
using System.Linq;

namespace Facepunch.CoreWars
{
	public partial class Game : Sandbox.Game
	{
		[Net] public StateSystem StateSystem { get; private set; }

		public static new Game Current { get; private set; }
		public static RootPanel Hud { get; private set; }

		public static T GetStateAs<T>() where T : BaseState
		{
			return Current.StateSystem.Active as T;
		}

		public Game()
		{
			if ( IsServer )
			{
				StateSystem = new();
			}

			Current = this;
		}

		public override void ClientSpawn()
		{
			Hud = new Hud();

			base.ClientSpawn();
		}

		public virtual void PlayerRespawned( Player player )
		{
			StateSystem.Active?.OnPlayerRespawned( player );
		}

		public override void OnKilled( Entity pawn )
		{
			if ( pawn is not Player player ) return;

			StateSystem.Active?.OnPlayerKilled( player, player.LastDamageTaken );
		}

		public override bool CanHearPlayerVoice( Client sourceClient, Client destinationClient )
		{
			return false;
		}

		public override void DoPlayerNoclip( Client client ) { }

		public override void DoPlayerSuicide( Client client ) { }

		public override void ClientDisconnect( Client client, NetworkDisconnectionReason reason )
		{
			InventorySystem.ClientDisconnected( client );
			StateSystem.Active?.OnPlayerDisconnected( client.Pawn as Player );
			base.ClientDisconnect( client, reason );
		}

		public override void ClientJoined( Client client )
		{
			base.ClientJoined( client );

			var player = new Player( client );
			player.LifeState = LifeState.Dead;

			VoxelWorld.Current.AddViewer( client );

			if ( VoxelWorld.Current.Initialized )
			{
				SendMapToClient( client);
			}
		}

		public override void PostLevelLoaded()
		{
			if ( !IsServer )
				return;

			StateSystem.Set( new LobbyState() );

			StartLoadMapTask();
		}
		
		private async void StartLoadMapTask()
		{
			var world = VoxelWorld.Create( 1337 );

			world.OnInitialized += OnMapInitialized;
			world.SetMaterials( "materials/procgen/voxel.vmat", "materials/procgen/voxel_translucent.vmat" );
			world.SetChunkRenderDistance( 4 );
			world.SetChunkUnloadDistance( 8 );
			world.SetChunkSize( 32, 32, 32 );
			world.SetSeaLevel( 48 );
			world.SetMaxSize( 256, 256, 128 );
			world.LoadBlockAtlas( "textures/blocks.json" );
			world.AddAllBlockTypes();
			world.SetMinimumLoadedChunks( 8 );

			world.SetChunkGenerator<PerlinChunkGenerator>();
			world.AddBiome<PlainsBiome>();
			world.AddBiome<WeirdBiome>();

			var startChunkSize = 4;

			for ( var x = 0; x < startChunkSize; x++ )
			{
				for ( var y = 0; y < startChunkSize; y++ )
				{
					await GameTask.Delay( 100 );

					var chunk = world.GetOrCreateChunk(
						x * world.ChunkSize.x,
						y * world.ChunkSize.y,
						0
					);

					_ = chunk.Initialize();
				}
			}

			await GameTask.Delay( 500 );

			world.Initialize();
		}

		private void OnMapInitialized()
		{
			var clients = Client.All.ToList();

			foreach ( var client in clients )
			{
				SendMapToClient( client );
			}
		}

		private void SendMapToClient( Client client )
		{
			VoxelWorld.Current.Send( client );

			if ( client.Pawn is Player )
			{
				var player = (client.Pawn as Player);
				StateSystem.Active?.OnPlayerJoined( player );
				player.OnMapLoaded();
			}
		}
	}
}
