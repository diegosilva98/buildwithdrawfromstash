﻿using Backtrace.Unity.Common;
using HarmonyLib;
using ProjectM;
using ProjectM.UI;
using Il2CppSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using VMods.Shared;
using Bloodstone.API;
using Il2CppSystem.Collections.Generic;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace VMods.ResourceStashWithdrawal
{
    [HarmonyPatch]
    public class UIClickHook
    {
        #region Variables

        private static DateTime _lastResourceRequest = DateTime.UtcNow;

        #endregion

        #region Public Methods

        public static void Reset()
        {
            _lastResourceRequest = DateTime.UtcNow;
        }

        [HarmonyPatch(typeof(GridSelectionEntry), nameof(GridSelectionEntry.OnPointerClick))]
        [HarmonyPostfix]
        public static void OnPointerClick(GridSelectionEntry __instance, PointerEventData eventData)
        {
            UITooltipHook.OnPointerEnter(__instance, eventData);
            if (!VWorld.IsClient || eventData.button != PointerEventData.InputButton.Middle ||
                DateTime.UtcNow.Subtract(_lastResourceRequest).TotalSeconds <= 0.2f)
            {
                return;
            }

            _lastResourceRequest = DateTime.UtcNow;

            bool withdrawFullAmount = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            var client = VWorld.Client;
            var entityManager = client.EntityManager;
            var gameDataSystem = client.GetExistingSystem<GameDataSystem>();
            var itemHashLookupMap = gameDataSystem.ItemHashLookupMap;
            var prefabCollectionSystem = client.GetExistingSystem<PrefabCollectionSystem>();
            var prefabLookupMap = prefabCollectionSystem.PrefabLookupMap;
            var userCharEntity = Entity.Null;
            foreach (var UsersEntity in entityManager.CreateEntityQuery(ComponentType.ReadOnly<User>())
                         .ToEntityArray(Allocator.Temp))
            {
                entityManager.TryGetComponentData<User>(UsersEntity, out var userComponent);
                userCharEntity = userComponent.LocalCharacter._Entity;
            }


            RefinementstationRecipeEntry refinementstationRecipeEntry =
                __instance.GetComponent<RefinementstationRecipeEntry>();
            RefinementstationRecipeItem refinementstationRecipeItem =
                __instance.GetComponent<RefinementstationRecipeItem>();
            WorkstationRecipeGridSelectionEntry workstationRecipeGridSelectionEntry =
                __instance.GetComponent<WorkstationRecipeGridSelectionEntry>();
            BuildMenu_StructureEntry buildMenuStructureEntry =
                __instance.GetComponent<BuildMenu_StructureEntry>();
            List<PrefabGUID> requiredItemGUIDs = null;
            if (refinementstationRecipeEntry != null)
            {
                var refinementstationSubMenu = __instance.GetComponentInParent<RefinementstationSubMenu>();
                var unitSpawnerstationSubMenu = __instance.GetComponentInParent<UnitSpawnerstationSubMenu>();
                if (refinementstationSubMenu != null)
                {
                    var recipe =
                        refinementstationSubMenu.RecipesSelectionGroup.Entries._items[
                            refinementstationRecipeEntry.EntryIndex];
                    foreach (var requirement in recipe.Requirements)
                    {
                        SendWithdrawRequest(refinementstationSubMenu.InputInventorySelectionGroup,
                            refinementstationSubMenu.OutputInventorySelectionGroup, recipe, requirement);
                    }
                }
                else if (unitSpawnerstationSubMenu != null)
                {
                    var recipe =
                        unitSpawnerstationSubMenu.RecipesSelectionGroup.Entries._items[
                            refinementstationRecipeEntry.EntryIndex];
                    foreach (var requirement in recipe.Requirements)
                    {
                        SendWithdrawRequest(unitSpawnerstationSubMenu.InputInventorySelectionGroup,
                            unitSpawnerstationSubMenu.OutputInventorySelectionGroup, recipe, requirement);
                    }
                }
                else
                {
#if DEBUG
                    Utils.Logger.LogMessage($"Unknown/unhandled SubMenu for Type: {__instance.GetScriptClassName()}");
#endif
                    return;
                }

                // Force update the tooltip
                UITooltipHook.OnPointerEnter(__instance, eventData);
            }
            else if (refinementstationRecipeItem != null)
            {
                refinementstationRecipeEntry =
                    refinementstationRecipeItem.GetComponentInParent<RefinementstationRecipeEntry>();

                var refinementstationSubMenu = __instance.GetComponentInParent<RefinementstationSubMenu>();
                var unitSpawnerstationSubMenu = __instance.GetComponentInParent<UnitSpawnerstationSubMenu>();
                if (refinementstationSubMenu != null)
                {
                    var recipe =
                        refinementstationSubMenu.RecipesSelectionGroup.Entries._items[
                            refinementstationRecipeEntry.EntryIndex];

                    foreach (var requirement in recipe.Requirements)
                    {
                        if (requirement.Guid == refinementstationRecipeItem.Guid)
                        {
                            SendWithdrawRequest(refinementstationSubMenu.InputInventorySelectionGroup,
                                refinementstationSubMenu.OutputInventorySelectionGroup, recipe, requirement);

                            // Force update the tooltip
                            UITooltipHook.OnPointerEnter(__instance, eventData);
                            return;
                        }
                    }

                    foreach (var output in recipe.OutputItems)
                    {
                        if (output.Guid == refinementstationRecipeItem.Guid)
                        {
                            int requiredAmount = output.Amount;
#if DEBUG
                            var name = Utils.GetItemName(output.Guid, gameDataSystem, entityManager, prefabLookupMap);
                            Utils.Logger.LogMessage(
                                $"Withdraw Recipe item: {requiredAmount}x {name} ({output.Guid.GuidHash})");
#endif
                            ResourceStashWithDrawalRequester.StartTask(new ResourceStashWithdrawalRequest()
                            {
                                ItemGUIDHash = output.Guid.GuidHash,
                                Amount = requiredAmount,
                            });
                            
                            // Force update the tooltip
                            UITooltipHook.OnPointerEnter(__instance, eventData);
                            return;
                        }
                    }
                }
                else if (unitSpawnerstationSubMenu != null)
                {
                    var recipe =
                        unitSpawnerstationSubMenu.RecipesSelectionGroup.Entries._items[
                            refinementstationRecipeEntry.EntryIndex];

                    foreach (var requirement in recipe.Requirements)
                    {
                        if (requirement.Guid == refinementstationRecipeItem.Guid)
                        {
                            SendWithdrawRequest(unitSpawnerstationSubMenu.InputInventorySelectionGroup,
                                unitSpawnerstationSubMenu.OutputInventorySelectionGroup, recipe, requirement);

                            // Force update the tooltip
                            UITooltipHook.OnPointerEnter(__instance, eventData);
                            return;
                        }
                    }

                    // Don't look at the output recipes, since those are units (and not inventory items)
                }
                else
                {
#if DEBUG
                    Utils.Logger.LogMessage(
                        $"Unknown/unhandled {nameof(refinementstationRecipeItem)} SubMenu for Type: {__instance.GetScriptClassName()}");
#endif
                    return;
                }
            }
            else if (workstationRecipeGridSelectionEntry != null)
            {
                var workstationSubMenu = __instance.GetComponentInParent<WorkstationSubMenu>();
                if (workstationSubMenu == null)
                {
                    // Only allow withdrawing when it's a workstation (and NOT when you're in your crafting tab of the Inventory Sub Menu!)
                    return;
                }

                float resourceMultiplier = 1f;
                // Hacky solution to find the bonus -> this is done because the 'BonusType' is incorrect/bugged.
                var bonuses = workstationSubMenu.BonusesSelectionGroup.Entries._items;
                var lastIndex = bonuses.Count - 1;
                var lastBonus = bonuses[lastIndex];
                if (lastBonus.Unlocked)
                {
                    resourceMultiplier = 1f - (lastBonus.Value / 100f);
                }

                var recipe =
                    workstationSubMenu.RecipesGridSelectionGroup.Entries._items[
                        workstationRecipeGridSelectionEntry.EntryIndex];
                if (gameDataSystem.RecipeHashLookupMap.ContainsKey(recipe.EntryId))
                {
                    var recipeData = gameDataSystem.RecipeHashLookupMap[recipe.EntryId];
                    var hasRecipeRequirementBuffer =
                        entityManager.TryGetBuffer<RecipeRequirementBuffer>(recipeData.Entity, out var requirements);
                    if (hasRecipeRequirementBuffer)
                    {
                        foreach (var requirement in requirements)
                        {
                            int requiredAmount = (int)Math.Ceiling(requirement.Amount * resourceMultiplier);
                            var itemGUID = requirement.Guid;
#if DEBUG
                            var name = Utils.GetItemName(itemGUID, gameDataSystem, entityManager, prefabLookupMap);
                            Utils.Logger.LogMessage($"Withdraw Recipe item: {requiredAmount}x {name} ({itemGUID})");
#endif
                            if (!withdrawFullAmount)
                            {
                                foreach (var stationItem in workstationSubMenu.ItemOutputGridSelectionGroup.Entries
                                             ._items)
                                {
                                    if (stationItem.EntryId == itemGUID)
                                    {
                                        requiredAmount -= stationItem.Stacks;
                                    }
                                }

                                requiredAmount -=
                                    InventoryUtilities.GetItemAmount(entityManager, userCharEntity, itemGUID);
                            }

                            if (requiredAmount > 0)
                            {
                                ResourceStashWithDrawalRequester.StartTask(new ResourceStashWithdrawalRequest()
                                {
                                    ItemGUIDHash = itemGUID.GuidHash,
                                    Amount = requiredAmount,
                                });
                            }
                        }

                        // Force update the tooltip
                        UITooltipHook.OnPointerEnter(__instance, eventData);
                    }
                }
            }
            else if(buildMenuStructureEntry != null)
            {
                if(gameDataSystem.BlueprintHashLookupMap.ContainsKey(buildMenuStructureEntry.PrefabGuid))
				{
					var blueprintData = gameDataSystem.BlueprintHashLookupMap[buildMenuStructureEntry.PrefabGuid];
					var hasBlueprintRequirementBuffer = entityManager.TryGetBuffer<BlueprintRequirementBuffer>(blueprintData.Entity, out var requirements);
					
					if(hasBlueprintRequirementBuffer)
					{
						requiredItemGUIDs = new();
						foreach(var requirement in requirements)
						{
                            int requiredAmount = (int)Math.Ceiling(requirement.Amount);
                            var itemGUID = requirement.PrefabGUID;
#if DEBUG
                            var name = Utils.GetItemName(itemGUID, gameDataSystem, entityManager, prefabLookupMap);
                            Utils.Logger.LogMessage($"Withdraw Recipe item: {requiredAmount}x {name} ({itemGUID})");
#endif
                            if (requiredAmount > 0)
                            {
                                ResourceStashWithDrawalRequester.StartTask(new ResourceStashWithdrawalRequest()
                                {
                                    ItemGUIDHash = itemGUID.GuidHash,
                                    Amount = requiredAmount,
                                });
                            }
                        }
                        // Force update the tooltip
                        UITooltipHook.OnPointerEnter(__instance, eventData);
					}
				}
                
                return;
            }
            else
            {
#if DEBUG
                Utils.Logger.LogMessage(
                    $"Unknown/unhandled {nameof(GridSelectionEntry)} Type: {__instance.GetScriptClassName()}");
#endif
                return;
            }

            // Nested Method(s)
            void SendWithdrawRequest(
                GridSelectionGroup<ItemGridSelectionEntry, ItemGridSelectionEntry.Data> inputInventorySelectionGroup,
                GridSelectionGroup<ItemGridSelectionEntry, ItemGridSelectionEntry.Data> outputInventorySelectionGroup,
                RefinementstationRecipeEntry.Data recipe, RecipeRequirementBuffer requirement)
            {
                int requiredAmount = (int)Math.Ceiling(requirement.Amount * recipe.ResourceMultiplier);
#if DEBUG
                var name = Utils.GetItemName(requirement.Guid, gameDataSystem, entityManager, prefabLookupMap);
                Utils.Logger.LogMessage(
                    $"next Withdraw Recipe item: {requiredAmount}x {name} ({requirement.Guid.GuidHash})");
#endif

                if (!withdrawFullAmount)
                {
                    foreach (var stationItem in inputInventorySelectionGroup.Entries._items)
                    {
                        if (stationItem.EntryId == requirement.Guid)
                        {
                            requiredAmount -= stationItem.Stacks;
                        }
                    }

                    foreach (var stationItem in outputInventorySelectionGroup.Entries._items)
                    {
                        Utils.Logger.LogMessage(
                            $"Output Station Item: {stationItem.Stacks}x {Utils.GetItemName(stationItem.EntryId, gameDataSystem, entityManager, prefabLookupMap)} ({stationItem.EntryId.GuidHash})");
                        if (stationItem.EntryId == requirement.Guid)
                        {
                            Utils.Logger.LogMessage(
                                $"Output Station Item: {Utils.GetItemName(stationItem.EntryId, gameDataSystem, entityManager, prefabLookupMap)} == {Utils.GetItemName(requirement.Guid, gameDataSystem, entityManager, prefabLookupMap)}");
                            requiredAmount -= stationItem.Stacks;
                        }
                    }

                    requiredAmount -= InventoryUtilities.GetItemAmount(entityManager, userCharEntity, requirement.Guid);
                }

                if (requiredAmount > 0)
                {
                    ResourceStashWithDrawalRequester.StartTask(new ResourceStashWithdrawalRequest()
                    {
                        ItemGUIDHash = requirement.Guid.GuidHash,
                        Amount = requiredAmount,
                    });
                }
            }
        }

        #endregion
    }
}