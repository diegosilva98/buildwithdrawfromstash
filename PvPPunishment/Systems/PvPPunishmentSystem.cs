﻿using ProjectM;
using ProjectM.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using VMods.Shared;
using Wetstone.API;

namespace VMods.PvPPunishment
{
	public static class PvPPunishmentSystem
	{
		#region Consts

		private const string PvPPunishmentFileName = "PvPPunishment.json";

		#endregion

		#region Variables

		private static Dictionary<ulong, OffenseData> _offenses;

		#endregion

		#region Public Methods

		public static void Initialize()
		{
			_offenses = VModStorage.Load(PvPPunishmentFileName, () => new Dictionary<ulong, OffenseData>());

			PruneOffenses();

			VModStorage.SaveEvent += Save;
			VampireDownedHook.VampireDownedEvent += OnVampireDowned;
			BuffSystemHook.ProcessBuffEvent += OnProcessBuff;
		}

		public static void Deinitialize()
		{
			BuffSystemHook.ProcessBuffEvent -= OnProcessBuff;
			VampireDownedHook.VampireDownedEvent -= OnVampireDowned;
			VModStorage.SaveEvent -= Save;
		}

		public static void Save()
		{
			PruneOffenses();

			VModStorage.Save(PvPPunishmentFileName, _offenses);
		}

		#endregion

		#region Private Methods

		private static void OnVampireDowned(Entity killer, Entity victim)
		{
			var entityManager = VWorld.Server.EntityManager;

			Entity killerUserEntity = entityManager.GetComponentData<PlayerCharacter>(killer).UserEntity._Entity;
			var killerUser = entityManager.GetComponentData<User>(killerUserEntity);
			ulong killerSteamID = killerUser.PlatformId;
			float killerLevel = HighestGearScoreSystem.GetCurrentOrHighestGearScore(new FromCharacter()
			{
				User = killerUserEntity,
				Character = killer,
			});

			Entity victimUserEntity = entityManager.GetComponentData<PlayerCharacter>(victim).UserEntity._Entity;
			var victimUser = entityManager.GetComponentData<User>(victimUserEntity);
			ulong victimSteamID = victimUser.PlatformId;
			float victimLevel = HighestGearScoreSystem.GetCurrentOrHighestGearScore(new FromCharacter()
			{
				User = victimUserEntity,
				Character = victim,
			});

			var diff = killerLevel - victimLevel;

#if DEBUG
			var msg = $"{killerUser.CharacterName} ({killerSteamID}) [Lv: {killerLevel}] killed {victimUser.CharacterName} ({victimSteamID}) [Lv: {victimLevel}]. - Diff: {diff}";
			Utils.Logger.LogMessage(msg);
			//Utils.SendMessage(killerUserEntity, msg, ServerChatMessageType.System);
			//Utils.SendMessage(victimUserEntity, msg, ServerChatMessageType.System);
#endif

			if(diff >= PvPPunishmentConfig.PvPPunishmentLevelDifference.Value)
			{
				if(!_offenses.TryGetValue(killerSteamID, out var offense))
				{
					offense = new OffenseData();
					_offenses.Add(killerSteamID, offense);
				}

#if DEBUG
				msg = $"Last Offense was at: {offense.LastOffenseTime}";
				Utils.Logger.LogMessage(msg);
				//Utils.SendMessage(killerUserEntity, msg, ServerChatMessageType.System);
				//Utils.SendMessage(victimUserEntity, msg, ServerChatMessageType.System);
#endif

				TimeSpan timeSpan = DateTime.UtcNow - offense.LastOffenseTime;

#if DEBUG
				msg = $"Time Diff since last offense: {timeSpan}";
				Utils.Logger.LogMessage(msg);
				//Utils.SendMessage(killerUserEntity, msg, ServerChatMessageType.System);
				//Utils.SendMessage(victimUserEntity, msg, ServerChatMessageType.System);
#endif
				if(timeSpan.TotalSeconds > PvPPunishmentConfig.PvPPunishmentOffenseCooldown.Value)
				{
					offense.OffenseCount = 1;
				}
				else
				{
					offense.OffenseCount++;
				}
				offense.LastOffenseTime = DateTime.UtcNow;

#if DEBUG
				msg = $"New Offense Count: {offense.OffenseCount}";
				Utils.Logger.LogMessage(msg);
				//Utils.SendMessage(killerUserEntity, msg, ServerChatMessageType.System);
				//Utils.SendMessage(victimUserEntity, msg, ServerChatMessageType.System);
#endif

				if(offense.OffenseCount >= PvPPunishmentConfig.PvPPunishmentOffenseLimit.Value)
				{
					Utils.ApplyBuff(killerUserEntity, killer, Utils.SevereGarlicDebuff);
#if DEBUG
					msg = $"Punishment applied for {killerUser.CharacterName} ({killerSteamID})";
					Utils.Logger.LogMessage(msg);
					//Utils.SendMessage(killerUserEntity, msg, ServerChatMessageType.System);
					//Utils.SendMessage(victimUserEntity, msg, ServerChatMessageType.System);
#endif
				}
#if DEBUG
				else
				{
					msg = $"Punishment count has increased!";
					Utils.Logger.LogMessage(msg);
					//Utils.SendMessage(killerUserEntity, msg, ServerChatMessageType.System);
					//Utils.SendMessage(victimUserEntity, msg, ServerChatMessageType.System);
				}
#endif
			}
#if DEBUG
			else
			{
				msg = $"No punishment appled -> kill was within appropriate level";
				Utils.Logger.LogMessage(msg);
				//Utils.SendMessage(killerUserEntity, msg, ServerChatMessageType.System);
				//Utils.SendMessage(victimUserEntity, msg, ServerChatMessageType.System);
			}
#endif
		}

		private static void OnProcessBuff(Entity entity, PrefabGUID buffGUID)
		{
			if(!VWorld.IsServer || buffGUID != Utils.SevereGarlicDebuff)
			{
				return;
			}

			var entityManager = VWorld.Server.EntityManager;

			var buffLifeTime = entityManager.GetComponentData<LifeTime>(entity);
			buffLifeTime.Duration = PvPPunishmentConfig.PvPPunishmentDuration.Value;
			entityManager.SetComponentData(entity, buffLifeTime);

			var buffer = entityManager.AddBuffer<ModifyUnitStatBuff_DOTS>(entity);
			TryAddReductionBuff(buffer, UnitStatType.MovementSpeed, ModificationType.Multiply, PvPPunishmentConfig.PvPPunishmentMovementSpeedReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.MaxHealth, ModificationType.Multiply, PvPPunishmentConfig.PvPPunishmentMaxHealthReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.PhysicalResistance, ModificationType.Add, PvPPunishmentConfig.PvPPunishmentPhysResistReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.SpellResistance, ModificationType.Add, PvPPunishmentConfig.PvPPunishmentSpellResistReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.FireResistance, ModificationType.Add, PvPPunishmentConfig.PvPPunishmentFireResistReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.HolyResistance, ModificationType.Add, PvPPunishmentConfig.PvPPunishmentHolyResistReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.SunResistance, ModificationType.Add, PvPPunishmentConfig.PvPPunishmentSunResistReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.SilverResistance, ModificationType.Add, PvPPunishmentConfig.PvPPunishmentSilverResistReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.PhysicalPower, ModificationType.Multiply, PvPPunishmentConfig.PvPPunishmentPhysPowerReduction.Value);
			TryAddReductionBuff(buffer, UnitStatType.SpellPower, ModificationType.Multiply, PvPPunishmentConfig.PvPPunishmentSpellPowerReduction.Value);
		}

		private static void TryAddReductionBuff(DynamicBuffer<ModifyUnitStatBuff_DOTS> buffer, UnitStatType unitStatType, ModificationType modificationType, float value)
		{
			if(value > 0f)
			{
				buffer.Add(new ModifyUnitStatBuff_DOTS()
				{
					StatType = unitStatType,
					Value = modificationType switch
					{
						ModificationType.Multiply => (100f - value) / 100f,
						ModificationType.Add => -value,
						_ => value,
					},
					ModificationType = modificationType,
					Id = ModificationId.NewId(0),
				});
			}
		}

		private static void PruneOffenses()
		{
			var now = DateTime.UtcNow;
			var keys = _offenses.Keys.ToList();
			var offenseCooldown = PvPPunishmentConfig.PvPPunishmentOffenseCooldown.Value;
			foreach(var key in keys)
			{
				var offenseData = _offenses[key];
				if(now.Subtract(offenseData.LastOffenseTime).TotalSeconds > offenseCooldown)
				{
					_offenses.Remove(key);
				}
			}
		}

		[Command("ispunished", "ispunished [<playername>]", "Tell you if the the given player (or yourself when no playername is given) currently has the PvP Punishment buff", true)]
		private static void OnIsPunishedPlayerCommand(Command command)
		{
			var entityManager = VWorld.Server.EntityManager;
			(var searchUsername, var fromCharacter) = command.GetFromCharacter(entityManager: entityManager);

			if(fromCharacter.HasValue)
			{
				if(BuffUtility.HasBuff(entityManager, fromCharacter.Value.Character, Utils.SevereGarlicDebuff))
				{
					command.User.SendSystemMessage($"Vampire <color=#ffffff>{searchUsername}</color> <color=#ff0000>is</color> currently punished.");
				}
				else
				{
					command.User.SendSystemMessage($"Vampire <color=#ffffff>{searchUsername}</color> <color=#00ff00>isn't</color> punished.");
				}
			}
			command.Use();
		}

		[Command("punish", "punish [<playername>]", "Adds (or refreshes) the PvP Punishment buff for the given player (or yourself when no playername is given)", true)]
		private static void OnPunishPlayerCommand(Command command)
		{
			var entityManager = VWorld.Server.EntityManager;
			(var searchUsername, var fromCharacter) = command.GetFromCharacter(entityManager: entityManager);

			if(fromCharacter.HasValue)
			{
				Utils.ApplyBuff(fromCharacter.Value, Utils.SevereGarlicDebuff);
				command.User.SendSystemMessage($"Vampire <color=#ffffff>{searchUsername}</color> has been punished.");
			}
			command.Use();
		}

		[Command("unpunish", "unpunish [<playername>]", "Removes the PvP Punishment buff for the given player (or yourself when no playername is given)", true)]
		private static void OnUnPunishPlayerCommand(Command command)
		{
			var entityManager = VWorld.Server.EntityManager;
			(var searchUsername, var fromCharacter) = command.GetFromCharacter(entityManager: entityManager);

			if(fromCharacter.HasValue)
			{
				Utils.RemoveBuff(fromCharacter.Value, Utils.SevereGarlicDebuff);
				command.User.SendSystemMessage($"Vampire <color=#ffffff>{searchUsername}</color> has been un-punished.");
			}
			command.Use();
		}

		#endregion

		#region Nested

		private class OffenseData
		{
			public DateTime LastOffenseTime { get; set; }
			public int OffenseCount { get; set; }
		}

		#endregion
	}
}
