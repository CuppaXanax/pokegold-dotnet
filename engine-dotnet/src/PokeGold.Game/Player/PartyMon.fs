namespace PokeGold.Game.Player

open PokeGold.Game.Data
open PokeGold.Game.Battle

/// A Pokémon in the persistent party (on the player's team).
/// All fields are preservation of the GSC save-file struct.
type PartyMon =
    { SpeciesId: int            // national dex number
      Nickname: string
      Level: int
      Exp: int
      Hp: int                   // current HP (may be < MaxHp or 0 if fainted)
      MaxHp: int
      Status: string            // "" = none, "PSN", "BRN", "FRZ", "PAR", "SLP"
      Moves: (int * int) list   // (moveId, currentPP) pairs, up to 4
      Dvs: int                  // packed DV byte (0..15 each, simplified)
      StatExp: int              // simplified stat exp (0 for debug)
      HeldItem: string option   // item constant or None
      OtName: string
      OtId: int
      Friendship: int }

/// A player's party — up to 6 PartyMon in order.
type Party = PartyMon list

module PartyMon =

    let private speciesOf (speciesId: int) : BaseStats option =
        Species.all |> Map.tryPick (fun _ s -> if s.Dex = speciesId then Some s else None)

    /// Derive MaxHp from species base stats and level (Gen-2 formula, DV=0, SE=0).
    let deriveMaxHp (speciesId: int) (level: int) : int =
        match speciesOf speciesId with
        | Some s -> BattleMon.calcHp s.Hp level
        | None -> 1

    /// Build a fresh PartyMon at full HP for the given species and level.
    let create (speciesId: int) (level: int) : PartyMon =
        let maxHp = deriveMaxHp speciesId level
        let name =
            Species.all
            |> Map.tryPick (fun k s -> if s.Dex = speciesId then Some k else None)
            |> Option.defaultValue (string speciesId)
        { SpeciesId = speciesId
          Nickname = name
          Level = level
          Exp = 0
          Hp = maxHp
          MaxHp = maxHp
          Status = ""
          Moves = []
          Dvs = 0
          StatExp = 0
          HeldItem = None
          OtName = "PLAYER"
          OtId = 0
          Friendship = 70 }

    /// Convert a PartyMon to a BattleMon for use in battle (seam for M13/M14).
    /// Move lookup is approximate until M13 wires the full move set.
    let toBattleMon (mon: PartyMon) : BattleMon =
        let species =
            speciesOf mon.SpeciesId
            |> Option.defaultWith (fun () ->
                { Dex = mon.SpeciesId; Name = string mon.SpeciesId
                  Hp = 45; Attack = 45; Defense = 45; Speed = 45; SpAttack = 45; SpDefense = 45
                  Type1 = 0; Type2 = 0 })
        let moveDatas =
            mon.Moves
            |> List.choose (fun (moveId, _pp) ->
                Moves.all |> Map.tryFind (string moveId))
            |> List.truncate 4
        let bm = BattleMon.ofSpecies species mon.Level moveDatas
        { bm with Hp = min mon.Hp bm.MaxHp }
