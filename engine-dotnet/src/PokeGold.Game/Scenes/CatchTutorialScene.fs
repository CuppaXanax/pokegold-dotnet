namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Debug
open PokeGold.Game.Battle
open PokeGold.Game.Player

/// Source-style automated catching demonstration used by Route 29's Dude.
/// It owns a temporary Rattata and one Poke Ball, drives the ordinary BattleScene
/// menus, and never mutates the real player's party, bag, or Pokédex.
type CatchTutorialScene(content: Content, wildSpecies: string, wildLevel: int) =
    let dude =
        BattleMon.ofSpecies
            (Species.byName "RATTATA")
            5
            [ Moves.byName "TACKLE" ]

    let wild =
        { BattleMon.ofSpecies
            (Species.byName wildSpecies)
            wildLevel
            [ Moves.byName "TACKLE" ] with
            Hp = 1
            Status = Sleep 2 }

    let mutable caught = false
    let mutable pulse = false

    let battle =
        BattleScene(
            content.Font,
            Battle.createWild dude wild 0u,
            bag = (Bag.empty |> Bag.add "POKE_BALL" 1),
            onCatch = (fun _ -> caught <- true))

    member _.Caught = caught
    member _.BattleSnapshot: RuntimeBattleSnapshot = battle.RuntimeSnapshot
    member _.CurrentBag = battle.CurrentBag

    member private _.AutomatedInput() =
        let snapshot = battle.RuntimeSnapshot
        let activeInput =
            if snapshot.MessageActive || not snapshot.PendingMessages.IsEmpty then
                { Buttons.none with A = true }
            elif snapshot.Mode = "CommandMenu" then
                if battle.CommandCursor = 2 then
                    { Buttons.none with A = true }
                else
                    { Buttons.none with Down = true }
            elif snapshot.Mode = "PackMenu" then
                { Buttons.none with A = true }
            else
                Buttons.none

        pulse <- not pulse
        if pulse then activeInput else Buttons.none

    interface Scene with
        member this.Update(_buttons: Buttons) =
            (battle :> Scene).Update(this.AutomatedInput())

        member _.Render(fb: Framebuffer) =
            (battle :> Scene).Render fb
