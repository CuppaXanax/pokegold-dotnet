namespace PokeGold.Game.Player

open PokeGold.Game.Battle
open PokeGold.Game.Data

module Breeding =

    [<Literal>]
    let private eggDitto = 13

    [<Literal>]
    let private eggNone = 15

    let private speciesById speciesId =
        Species.all |> Map.tryPick (fun _ species -> if species.Dex = speciesId then Some species else None)

    let private isDitto (species: BaseStats) =
        species.Name = "DITTO"

    let private isUndiscovered (species: BaseStats) =
        species.EggGroup1 = eggNone && species.EggGroup2 = eggNone

    let private sharesEggGroup (a: BaseStats) (b: BaseStats) =
        [ a.EggGroup1; a.EggGroup2 ]
        |> List.exists (fun group -> group = b.EggGroup1 || group = b.EggGroup2)

    /// Implements CheckBreedmonCompatibility's hard compatibility gate. The
    /// source's DV/OT-ID calculation controls egg odds only, not eligibility.
    let compatible (a: PartyMon) (b: PartyMon) : bool =
        match speciesById a.SpeciesId, speciesById b.SpeciesId with
        | Some speciesA, Some speciesB when not (isUndiscovered speciesA || isUndiscovered speciesB) ->
            let dittoA = isDitto speciesA
            let dittoB = isDitto speciesB
            let groupsCompatible = dittoA || dittoB || sharesEggGroup speciesA speciesB

            if not groupsCompatible || (dittoA && dittoB) then
                false
            elif dittoA || dittoB then
                true
            else
                match BattleMon.genderFromDvs speciesA a.Dvs, BattleMon.genderFromDvs speciesB b.Dvs with
                | Male, Female
                | Female, Male -> true
                | _ -> false
        | _ -> false

    let private moveId name =
        MovesData.byIndex |> Array.tryFindIndex (fun move -> move.Name = name)

    let private moveSlots (moveNames: string list) =
        moveNames
        |> List.distinct
        |> List.truncate 4
        |> List.choose (fun name ->
            moveId name |> Option.map (fun id -> id, MovesData.byIndex.[id].Pp))

    let private levelUpMoves (speciesName: string) (hatchLevel: int) =
        EvosAttacksAccess.forSpecies speciesName
        |> Option.map (fun data ->
            data.Learnset
            |> List.filter (fun entry -> entry.Level <= hatchLevel)
            |> List.map (fun entry -> entry.Move))
        |> Option.defaultValue []

    let private inheritedMoves (offspring: BaseStats) (father: PartyMon) =
        let eggMoves = EggMoves.forSpecies offspring.Name

        let tmHmMoves =
            TmHmData.compatibleMovesBySpecies
            |> Map.tryFind offspring.Name
            |> Option.defaultValue Set.empty

        father.Moves
        |> List.choose (fun (id, _) -> Moves.tryByIndex id |> Option.map (fun move -> move.Name))
        |> List.filter (fun move -> Set.contains move eggMoves || Set.contains move tmHmMoves)

    /// Generate an egg from two parents. The egg is the base species of the mother.
    let generateEgg (mon1: PartyMon) (mon2: PartyMon) : PartyMon =
        let offspringSpecies =
            match speciesById mon1.SpeciesId, speciesById mon2.SpeciesId with
            | Some mother, Some father when isDitto mother && not (isDitto father) -> mon2.SpeciesId
            | _ -> mon1.SpeciesId

        match speciesById offspringSpecies with
        | Some offspring ->
            let moves =
                levelUpMoves offspring.Name 5
                @ inheritedMoves offspring mon2
                |> moveSlots
            let egg = PartyMon.create offspringSpecies 5
            { egg with Nickname = "EGG"; Friendship = 0; Moves = moves }
        | None ->
            let egg = PartyMon.create offspringSpecies 5
            { egg with Nickname = "EGG"; Friendship = 0 }

    let isEgg (mon: PartyMon) : bool =
        mon.Nickname = "EGG"

    /// Gen 2's community-verified conversion from base-stat hatch cycles to
    /// overworld steps is 256 steps per cycle; the source stores only the cycles.
    let hatchStepsFor (speciesId: int) : int =
        speciesById speciesId
        |> Option.map (fun species -> species.HatchCycles * 256)
        |> Option.defaultValue 0
