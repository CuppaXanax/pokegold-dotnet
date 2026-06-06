namespace PokeGold.Game.Core

/// Gen-2 time of day, derived from the real wall clock.
/// Source: engine/rtc.asm, constants/pokemon_data_constants.asm
type TimeOfDay = Morn | Day | Nite

module TimeOfDay =
    /// Derive time of day from an hour (0-23).
    let fromHour (hour: int) : TimeOfDay =
        if hour >= 4 && hour < 10 then Morn
        elif hour >= 10 && hour < 18 then Day
        else Nite

    /// Get the current time of day from the system clock.
    let current () : TimeOfDay =
        fromHour System.DateTime.Now.Hour

    /// Convert to the index used by encounter tables (0=morn, 1=day, 2=nite).
    let toIndex (tod: TimeOfDay) : int =
        match tod with
        | Morn -> 0
        | Day -> 1
        | Nite -> 2

    /// Convert to the value the script VM's `checktime` should set.
    /// GSC uses: MORN=0, DAY=1, NITE=2 (same as our index).
    let toScriptVar (tod: TimeOfDay) : int = toIndex tod
