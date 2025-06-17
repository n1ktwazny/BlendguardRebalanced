using BepInEx;
using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine.AI;

[BepInPlugin("com.nikt.BlendGuardRebalance", "BlenderGuard Rebalance", "0.1.1")]
[BepInDependency("com.nikt.BlendGuardGFixes", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("com.nikt.BlendguardGuidePlus", BepInDependency.DependencyFlags.SoftDependency)]

public class BlendRebalance : BaseUnityPlugin{

    private enum costTypes{
        normal,
        merge,
        mixedMerge
    }
    
    private static ConfigEntry<bool> enableRebalance;
    private static ConfigEntry<bool> enableDebugger;
    private static ConfigEntry<costTypes> costType; 
    private static ConfigEntry<bool> speedRampUp;
    public static TowerStatsClass[] UpdatedTowerStats = new TowerStatsClass[22];
    public static float CurrentGameModifier = 1f;
    
    void Awake(){
        enableRebalance = Config.Bind("Towers", "Enable Tower Rebalancing", true, "Enable the tower rebalances");
        costType = Config.Bind("Towers", "Enable Cost Merging", costTypes.mixedMerge, "mixedMerge = Prices increase for all towers, but the tower that are NOT currently being placed increase less, merge= All prices scale equally(not recommended, normal= keeps vanilla prices & scaling)");
        enableDebugger = Config.Bind("Debug", "Enable Debugger", false, "Share the mod logs");
        speedRampUp = Config.Bind("Invaders", "Speed Ramp Up", true, "Slowly makes new invaders faster as time progresses");

        AccessTools.Method(typeof(SceneManager), "ChangeScene", new Type[] { typeof(int) });
        Logger.LogInfo("Blendguard Rebalance Loading");
        Harmony.CreateAndPatchAll(typeof(BlendRebalance));
        Logger.LogInfo("Blendguard Rebalance Loaded");
        if (!Chainloader.PluginInfos.ContainsKey("com.nikt.BlendguardGFixes")){
            Logger.LogWarning("\n\nIt is recommended to use 'Blendguard General Fixes' mod for a better experience!!!\n");
        }
        
        ApplyRebalances();
        if (enableDebugger.Value) DebugTriggerRebalances();
    }
    
    [HarmonyPatch(typeof(TimeSurvivedManager), "Update")]
    [HarmonyPostfix]
    static void UpdateSpeedMod(TimeSurvivedManager __instance){
        if(speedRampUp.Value){
            CurrentGameModifier = Mathf.Clamp(__instance.TimeSurvived/60f/30f, 1f, 50f);
        }
    }

    [HarmonyPatch(typeof(Invader), "AssignAgentSpeed")]
    [HarmonyPostfix]
    static void AdditionalSpeed(Invader __instance){
        var enemyNav = AccessTools.Field(typeof(Invader), "_navMeshAgent").GetValue(__instance);
        NavMeshAgent navAgent = (NavMeshAgent)enemyNav;
        navAgent.speed *= CurrentGameModifier;
        if (enableDebugger.Value) Debug.Log($"Added a speed multilier of: {CurrentGameModifier} to get {navAgent.speed}");
    }

    [HarmonyPatch(typeof(UIManager), "UpdateCardCost")]
    [HarmonyPrefix]
    static void UpdateCardCost(UIManager __instance, ref int cost){
        var cardCosts = AccessTools.Field(typeof(UIManager), "_cardCosts").GetValue(__instance);
        AccessTools.Field(typeof(UIManager), "_cardCosts").SetValue(__instance, cardCosts);
    }
    
    private static readonly MethodInfo Transaction = AccessTools.Method(typeof(CurrencyManager), "Transaction");
    //Tower costs scaling Override
[HarmonyPatch(typeof(CurrencyManager), "Start")]
[HarmonyPrefix]
static bool OverrideCostScaling(CurrencyManager __instance){
    if (costType.Value == costTypes.normal) return true;
    
    var cardcostscale = AccessTools.Field(typeof(CurrencyManager), "_cardCostsScaler");
    var maxCardCost = AccessTools.Field(typeof(CurrencyManager), "_maxCardCosts");
    var maxCardCostScaling = AccessTools.Field(typeof(CurrencyManager), "_maxCardCostsScaling");
    // Set the values on the actual instance that's being patched

    int startcost=0;
    int startmoney=0;
    if (costType.Value == costTypes.merge){
        cardcostscale.SetValue(__instance, new int[] { 65, 75, 90 });
        maxCardCost.SetValue(__instance, new int[] { 10000, 10000, 10000 });
        maxCardCostScaling.SetValue(__instance, new int[] { 300, 400, 650 });
        startcost = 50;
        startmoney = 350;
    }else if (costType.Value == costTypes.mixedMerge){
        cardcostscale.SetValue(__instance, new int[] { 175, 300, 200 });
        maxCardCost.SetValue(__instance, new int[] { 15000, 15000, 30000 });
        maxCardCostScaling.SetValue(__instance, new int[] { 300, 500, 700 });
        startcost = 100;
        startmoney = 300;
    }
    for (int i = 0; i < __instance._cardCosts.Length; i++){
        __instance._cardCosts[i] = startcost;
        MonoSingleton<UIManager>.Instance.UpdateCardCost(startcost, i);
    }
    MonoSingleton<CurrencyManager>.Instance.Transaction(startmoney);
    Debug.Log("Card cost logic overriden :P.");
    return false;
    
}

//CostRaise Override - Fixed to handle property instead of field
[HarmonyPatch(typeof(CurrencyManager), "RaiseCardCost")]
[HarmonyPrefix]
static bool RaiseCost(CurrencyManager __instance, ref int cardIndex){
        if (costType.Value != costTypes.normal){
            // Get the variable fields
            var maxCardCostsField = AccessTools.Field(typeof(CurrencyManager), "_maxCardCosts");
            var maxCardCostsScalingField = AccessTools.Field(typeof(CurrencyManager), "_maxCardCostsScaling");
            var cardCostScalerField = AccessTools.Field(typeof(CurrencyManager), "_cardCostsScaler");
            
            // Turn fields to actual usable vars
            var cardCosts = __instance._cardCosts;
            var maxCardCosts = (int[])maxCardCostsField.GetValue(__instance);
            var maxCardCostsScaling = (int[])maxCardCostsScalingField.GetValue(__instance);
            var cardCostsScaling = (int[])cardCostScalerField.GetValue(__instance);

            if(costType.Value == costTypes.merge){
                cardCosts[0] = cardCostsScaling[cardIndex] + (int)Mathf.Round(cardCosts[0] * 1.15f);

                if (cardCosts[0] >= maxCardCosts[0]){
                    cardCosts[0] = maxCardCosts[0];
                    maxCardCosts[0] += maxCardCostsScaling[cardIndex];
                }
                cardCosts[1] = cardCosts[0];
                cardCosts[2] = cardCosts[0];
            }else if (costType.Value == costTypes.mixedMerge)
            {
                int oldTowerCost = cardCosts[cardIndex];
                cardCosts[cardIndex] = cardCostsScaling[cardIndex] + (int)Mathf.Round(cardCosts[0] * 1.2f);
                if (cardCosts[cardIndex] >= maxCardCosts[cardIndex]){
                    cardCosts[cardIndex] = maxCardCosts[cardIndex];
                    maxCardCosts[cardIndex] += maxCardCostsScaling[cardIndex];
                }

                int _index = 0;
                foreach (var tower in cardCosts){
                    if (tower != cardCosts[cardIndex]){
                        cardCosts[_index] += (cardCosts[cardIndex] - oldTowerCost)/4;
                    }
                    _index++;
                }
            }

            //UI update
            MonoSingleton<UIManager>.Instance.UpdateCardCost(cardCosts[0], 0);
            MonoSingleton<UIManager>.Instance.UpdateCardCost(cardCosts[1], 1);
            MonoSingleton<UIManager>.Instance.UpdateCardCost(cardCosts[2], 2);

            if (enableDebugger.Value) Debug.Log($"Tower Cost raised to: {cardCosts[cardIndex]}");
            return false;
        }
        return true;
}

    static void Main()
    {
    }

    static void DebugTriggerRebalances(){
        if (!enableDebugger.Value) return;
        Debug.Log("------Rebalance Debugger------");
        var towers = Resources.FindObjectsOfTypeAll<StructureInfo>();
        foreach (var tower in towers){
            Debug.Log(tower.structureName);
            Debug.Log("HP: " + tower.maxHealth);
            Debug.Log("Regen: " + tower.regeneration);
            Debug.Log("Damage: " + tower.damage);
            Debug.Log("Generation: " + tower.generation);
            Debug.Log("FireRate: " + tower.fireRate);
            Debug.Log("Crafting1: " + tower.primaryStructureInfo);
            Debug.Log("Crafting2: " + tower.secondaryStructureInfo + "\n\n");

            //var maxHp = AccessTools.Property(typeof(Structure), "_maxHealth");
            //Debug.Log(maxHp.GetValue(maxHp) + "\n");    
            //maxHp.SetValue(maxHp, 1);
        }
    }

    //Trigger for the damn rebalances
    static void ApplyRebalances(){
        if (!enableRebalance.Value)return;
        if (enableDebugger.Value) DebugTriggerRebalances();
        UpdatedTowerStats[0] = new TowerStatsClass().updateTowerStats("Blaster", TowerStatsClass.TowerTypes.R, 0, 100, 2, 2, 0.55f, 0, 1.65f, 0);
        UpdatedTowerStats[1] = new TowerStatsClass().updateTowerStats("Striker", TowerStatsClass.TowerTypes.R, 1, 180, 4, 4, 0.5f, 0, 1.65f, 0);
        UpdatedTowerStats[2] = new TowerStatsClass().updateTowerStats("Enforcer", TowerStatsClass.TowerTypes.R, 2, 300, 7, 7, 0.4f, 0, 1.65f, 0);
        UpdatedTowerStats[3] = new TowerStatsClass().updateTowerStats("Punisher", TowerStatsClass.TowerTypes.R, 3, 500, 12, 12, 0.3f, 0, 1.65f, 0);
        
        UpdatedTowerStats[4] = new TowerStatsClass().updateTowerStats("Guardian", TowerStatsClass.TowerTypes.P, 1, 550, 10, 5, 0.8f, 0, 1f, 0);
        UpdatedTowerStats[5] = new TowerStatsClass().updateTowerStats("Sentinel", TowerStatsClass.TowerTypes.P, 2, 1000, 17, 11, 0.6f, 0, 1f, 0);
        UpdatedTowerStats[6] = new TowerStatsClass().updateTowerStats("Vanguard", TowerStatsClass.TowerTypes.P, 3, 1300, 22, 15, 0.375f, 0, 1f, 0);
        
        UpdatedTowerStats[7] = new TowerStatsClass().updateTowerStats("Aegis", TowerStatsClass.TowerTypes.B, 0, 375, 11, 0, 0f, 0, 0f, 0);
        UpdatedTowerStats[8] = new TowerStatsClass().updateTowerStats("Bulwark", TowerStatsClass.TowerTypes.B, 1, 750, 23, 0, 0f, 0, 0f, 0);
        UpdatedTowerStats[9] = new TowerStatsClass().updateTowerStats("Bastion", TowerStatsClass.TowerTypes.B, 2, 1125, 35, 0, 0f, 0, 0f, 0);
        UpdatedTowerStats[10] = new TowerStatsClass().updateTowerStats("Fortress", TowerStatsClass.TowerTypes.B, 3, 1600, 50, 0, 0f, 0, 0f, 0);
        
        UpdatedTowerStats[11] = new TowerStatsClass().updateTowerStats("Warden", TowerStatsClass.TowerTypes.C, 1, 625, 11, 0, 0f, 55, 0f, 0);
        UpdatedTowerStats[12] = new TowerStatsClass().updateTowerStats("Rampart", TowerStatsClass.TowerTypes.C, 2, 975, 20, 0, 0f, 280, 0f, 0);
        UpdatedTowerStats[13] = new TowerStatsClass().updateTowerStats("Stronghold", TowerStatsClass.TowerTypes.C, 3, 1400, 35, 0, 0f, 980, 0f, 0);
        
        UpdatedTowerStats[14] = new TowerStatsClass().updateTowerStats("Pulse", TowerStatsClass.TowerTypes.G, 0, 200, 5, 0, 0f, 15, 0f, 0);
        UpdatedTowerStats[15] = new TowerStatsClass().updateTowerStats("Core", TowerStatsClass.TowerTypes.G, 1, 425, 11, 0, 0f, 70, 0f, 0);
        UpdatedTowerStats[16] = new TowerStatsClass().updateTowerStats("Amplifier", TowerStatsClass.TowerTypes.G, 2, 600, 16, 0, 0f, 300, 0f, 0);
        UpdatedTowerStats[17] = new TowerStatsClass().updateTowerStats("Reactor", TowerStatsClass.TowerTypes.G, 3, 850, 23, 0, 0f, 1000, 0f, 0);
        
        UpdatedTowerStats[18] = new TowerStatsClass().updateTowerStats("Conduit", TowerStatsClass.TowerTypes.Y, 1, 250, 4, 2, 0.36f, 60, 1f, 60);
        UpdatedTowerStats[19] = new TowerStatsClass().updateTowerStats("Harvester", TowerStatsClass.TowerTypes.Y, 2, 425, 8, 3, 0.25f, 260, 1f, 440);
        UpdatedTowerStats[20] = new TowerStatsClass().updateTowerStats("Reaper", TowerStatsClass.TowerTypes.Y, 3, 700, 11, 4, 0.15f, 800, 1f, 1400);
        
        UpdatedTowerStats[21] = new TowerStatsClass().updateTowerStats("Base", TowerStatsClass.TowerTypes.None, 0, 750, 5, 0, 0f, 10, 0f, 0);
        
        var towers = Resources.FindObjectsOfTypeAll<StructureInfo>();
        foreach (var tower in towers) { foreach (var newStats in UpdatedTowerStats){
                if (tower.structureName == newStats.TowerOverride){
                    tower.maxHealth = newStats.MaxHp;
                    tower.regeneration = newStats.Regen;
                    tower.fireRate = newStats.FireRate;
                    tower.damage = newStats.Damage;
                    tower.generation = newStats.CGeneration;
                }
        }}
    }

    //[HarmonyPatch(typeof(CurrencyManager), "RaiseCardCost")]
    //static bool RaiseCardCost(CurrencyManager __instance){}
    
    /*
     *
     * MOST OF THE DATA BELOW THIS POINT IS NOT FUNCTIONING,
     * 
     * the story goes that I was adding all of this noise for a feature that actually
     * already existed and accounting for it most likely lead to me finishing this mod
     * around about NEVER, because burnout is close. Uhh, so, yeah..
     * Do with that whatever you'd like.
     * 
     */
    
    [HarmonyPatch(typeof(Structure), "Start")]
    [HarmonyPostfix]
    static void Postfix(Structure __instance){
    
    var structureInfoObj = AccessTools.Field(typeof(Structure), "_structureInfo").GetValue(__instance);
    StructureInfo structureInfo = (StructureInfo)structureInfoObj;
    
    // ADDING CUSTOM DATA TO TOWER
    if (__instance.GetComponent<AttackerFiring>() != null){
        string structureName = structureInfo.structureName;
        var additionalTowerData = __instance.gameObject.AddComponent<AdditionalTowerData>();

        bool addedStats = false;
        foreach (var towerStatsInstance in UpdatedTowerStats){
            if (towerStatsInstance.TowerOverride == structureName){ 
                additionalTowerData.moneyOnKill = towerStatsInstance.MoneyOnKill; 
                additionalTowerData.towerRange = towerStatsInstance.TowerRange;
                additionalTowerData.hasSplash = towerStatsInstance.HasSplashAttack;
                addedStats = true;
            }
        }
        if (!addedStats){
            GameObject.Destroy(additionalTowerData);
        }
        
        if (enableDebugger.Value) Debug.Log($"Applied custom stats to {structureName} with value {additionalTowerData}");
    }
}

// Copy additional data to projectile
[HarmonyPatch(typeof(AttackerFiring), "ChooseTarget")]
[HarmonyPostfix]
static void ChooseTarget(AttackerFiring __instance){
    AdditionalTowerData sourceEffect = __instance.GetComponent<AdditionalTowerData>();
    if (sourceEffect != null){
        if (enableDebugger.Value) Debug.Log($"Found Tower Data Source.");
        var targetObj = AccessTools.Field(typeof(AttackerFiring), "_target").GetValue(__instance);
        if (targetObj == null){ return; }
        if (enableDebugger.Value) Debug.Log($"Found Tower Target.");

        Collider target = (Collider)targetObj;
        GameObject targetGameObject = target.gameObject;

        if (sourceEffect != null){
            CloneDataToProjectile(sourceEffect, targetGameObject);
        }
    }
}

// Copy OHE from projectile to Invader
[HarmonyPatch(typeof(AttackerProjectile), "OnTriggerEnter")]
[HarmonyPostfix]
static void EnemyTriggerEnter(AttackerProjectile __instance, Collider other)
{
    // Check if the collision is with an Invader
    if (other != null && other.CompareTag("Invader")){
        AdditionalProjectileData projectileEffect = __instance.GetComponent<AdditionalProjectileData>();
        if (projectileEffect != null){
            CloneProjectileToEnemy(projectileEffect, other.gameObject);
        }
    }
}

// Execute the death money gain for the Invaders
[HarmonyPatch(typeof(Invader), "Die")]
[HarmonyPrefix]
static void DeathMonyAdder(Invader __instance){
    AdditionalEnemyData enemyData = __instance.GetComponent<AdditionalEnemyData>();
    
    if (enemyData != null){
        CurrencyManager[] currencyManagers = Resources.FindObjectsOfTypeAll<CurrencyManager>();
        currencyManagers[0].Transaction(enemyData.moneyOnDeath, true);
        
        if (enableDebugger.Value) Debug.Log($"Money earned from kill: {enemyData.moneyOnDeath}");
    }
}


static void CloneDataToProjectile(AdditionalTowerData towerSource, GameObject targetObject){
    if (towerSource == null || targetObject == null){
        Debug.LogError("No source effect or target object in Tower>Projectile Clone");
        return;
    }
    
    AdditionalProjectileData newEffect = targetObject.AddComponent<AdditionalProjectileData>();
    newEffect.moneyOnKill = towerSource.moneyOnKill;
    newEffect.hasSplash = towerSource.hasSplash;
    
    if (enableDebugger.Value){
        Debug.Log($"Cloned to {targetObject.name} from {towerSource.name}");
    }
}

static void CloneProjectileToEnemy(AdditionalProjectileData projSource, GameObject enemy){
    if (projSource == null || enemy == null){
        Debug.LogError("No source effect or target object in Projectile>EnemyData Clone");
    }

    AdditionalEnemyData newEnemyData = enemy.AddComponent<AdditionalEnemyData>();
    newEnemyData.moneyOnDeath = projSource.moneyOnKill;
}

public class AdditionalTowerData : MonoBehaviour{
    public float towerRange = 1f; //unused
    public bool hasSplash = false; //unused
    public int moneyOnKill;
}

public class AdditionalProjectileData : MonoBehaviour{
    public int moneyOnKill = 0;
    public bool hasSplash = false; 
}}

public class AdditionalEnemyData : MonoBehaviour{ 
    public int moneyOnDeath = 0;
}

public class TowerStatsClass{
    [Header("Tower to override")] public string TowerOverride = "Placeholder"; //Insert name to override *some* tower

    public enum TowerTypes{ None = 0, R = 1, G = 2, Y = 3, B = 4, P = 5, C = 6 }

    public TowerTypes TwType;
    public int TwLvl;

    [Space] public int MaxHp;
    public int Regen;
    public int Damage;
    public float FireRate;

    public int CGeneration;

    //Additional Range
    public float TowerRange; //UNUSED

    //Splash damage
    public bool HasSplashAttack; //UNUSED
    public float SplashRange; //UNUSED

    //Currency on kill
    public int MoneyOnKill;

    public TowerStatsClass updateTowerStats(string tower, TowerTypes type, int towerLvl, int hp, int regen, int damage, float fireRate, int cGeneration, float towerRange, int moneyOnKill){
        TowerOverride = tower;
        TwLvl = towerLvl;
        TwType = type;
        MaxHp = hp;
        Regen = regen;
        Damage = damage;
        FireRate = fireRate;
        CGeneration = cGeneration;
        TowerRange = towerRange;
        MoneyOnKill = moneyOnKill;
        Debug.Log("Created stats for " + tower);
        return this;
    }
}