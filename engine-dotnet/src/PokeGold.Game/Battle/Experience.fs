namespace PokeGold.Game.Battle

/// Gen-2 experience growth curves and EXP calculations.
/// Source: data/growth_rates.asm, engine/pokemon/experience.asm
module Experience =

    /// Calculate EXP gained from defeating an enemy.
    /// Source: engine/battle/core.asm::GiveExperiencePoints
    let expGained (enemyBaseExp: int) (enemyLevel: int) (isTrainer: bool) : int =
        let raw = enemyBaseExp * enemyLevel / 7
        if isTrainer then raw * 3 / 2 else raw

    /// Calculate money earned from a trainer battle.
    let moneyEarned (baseReward: int) (lastMonLevel: int) : int =
        4 * baseReward * lastMonLevel

    let applyAmuletCoin (hasAmuletCoin: bool) (reward: int) : int =
        if hasAmuletCoin then reward * 2 else reward

    let private clampExp value = if value < 0 then 0 else value

    /// Total EXP needed to reach the given level for a growth rate (0-5).
    let expForLevel (growthRate: int) (level: int) : int =
        let n = max 0 level
        let n2 = n * n
        let n3 = n * n * n

        let totalExp =
            match growthRate with
            | 0 -> n3
            | 1 -> 3 * n3 / 4 + 10 * n2 - 30
            | 2 -> 3 * n3 / 4 + 20 * n2 - 70
            | 3 -> 6 * n3 / 5 - 15 * n2 + 100 * n - 140
            | 4 -> 4 * n3 / 5
            | 5 -> 5 * n3 / 4
            | _ -> n3

        clampExp totalExp

    /// EXP needed to go from current level to the next.
    let expToNextLevel (growthRate: int) (currentLevel: int) : int =
        if currentLevel >= 100 then 0
        else expForLevel growthRate (currentLevel + 1) - expForLevel growthRate currentLevel

    /// Given a growth rate, current EXP, and EXP gained, compute the new level.
    /// Caps at 100.
    let levelAfterExp (growthRate: int) (currentLevel: int) (currentExp: int) (gained: int) : int * int =
        let totalExp = currentExp + gained
        let mutable lvl = currentLevel

        while lvl < 100 && totalExp >= expForLevel growthRate (lvl + 1) do
            lvl <- lvl + 1

        (lvl, totalExp)
