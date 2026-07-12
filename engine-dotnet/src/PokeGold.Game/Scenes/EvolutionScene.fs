namespace PokeGold.Game.Scenes

open PokeGold.Game.Core
open PokeGold.Game.Data
open PokeGold.Game.Render
open PokeGold.Game.Ui

/// The result of the cancellable evolution presentation. The caller owns all
/// persistent mutation so cancellation can leave the Pokemon untouched.
type EvolutionDecision =
    | AcceptEvolution
    | CancelEvolution

/// Cancellable evolution prompt shaped after engine/pokemon/evolve.asm: the
/// evolution is pending until its presentation finishes, and B stops it.
type EvolutionScene(font: Font, nickname: string, targetSpecies: string, onDecision: EvolutionDecision -> unit) =
    let input = EdgeDetector()
    let palette = TextRenderer.palette
    let mutable finished = false

    let complete decision =
        if not finished then
            finished <- true
            onDecision decision
        Pop

    interface Scene with
        member _.Update(buttons: Buttons) : Transition =
            if finished then
                Pop
            else
                let edges = input.Update buttons
                if edges.B then complete CancelEvolution
                elif edges.A then complete AcceptEvolution
                else Stay

        member _.Render(fb: Framebuffer) =
            WindowRenderer.drawBox fb font palette 1 12 18 5
            WindowRenderer.drawString fb font palette 2 13 (sprintf "What? %s is" nickname)
            WindowRenderer.drawString fb font palette 2 14 "evolving!"
            WindowRenderer.drawString fb font palette 2 15 (sprintf "Into %s" targetSpecies)
