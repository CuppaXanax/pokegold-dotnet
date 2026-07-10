namespace PokeGold.Game.Player

open System
open PokeGold.Game.Data
open PokeGold.Game.Battle

/// A Pokémon in the persistent party (on the player's team).
/// All fields are preservation of the GSC save-file struct.
type PartyMon =
    { Id: Guid                  // stable native-engine identity (not a ROM field)
      SpeciesId: int            // national dex number
      Nickname: string
      Level: int
      Exp: int
      Hp: int                   // current HP (may be < MaxHp or 0 if fainted)
      MaxHp: int
      Status: string            // ""/PSN/BRN/FRZ/PAR or SLP:n (legacy SLP accepted)
      Moves: (int * int) list   // (moveId, currentPP) pairs, up to 4
      Dvs: int                  // packed Attack/Defense/Speed/Special nibbles
      StatExp: StatExperience   // five 16-bit HP/Atk/Def/Speed/Special words
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

    let deriveMaxHpWith (speciesId: int) (level: int) (dvs: int) (statExp: StatExperience) : int =
        match speciesOf speciesId with
        | Some s -> (BattleMon.calculateStats s level dvs statExp).MaxHp
        | None -> 1

    /// Build a fresh PartyMon at full HP for the given species and level.
    let createWithDvs (speciesId: int) (level: int) (dvs: int) : PartyMon =
        let maxHp = deriveMaxHpWith speciesId level dvs StatExperience.zero
        let name =
            Species.all
            |> Map.tryPick (fun k s -> if s.Dex = speciesId then Some k else None)
            |> Option.defaultValue (string speciesId)
        { Id = Guid.NewGuid()
          SpeciesId = speciesId
          Nickname = name
          Level = level
          Exp = 0
          Hp = maxHp
          MaxHp = maxHp
          Status = ""
          Moves = []
          Dvs = dvs
          StatExp = StatExperience.zero
          HeldItem = None
          OtName = "PLAYER"
          OtId = 0
          Friendship = 70 }

    let create (speciesId: int) (level: int) : PartyMon =
        createWithDvs speciesId level 0

    /// Recalculate level-dependent HP while preserving the current damage deficit.
    let withLevel (level: int) (mon: PartyMon) : PartyMon =
        let newMaxHp = deriveMaxHpWith mon.SpeciesId level mon.Dvs mon.StatExp
        let hpGain = newMaxHp - mon.MaxHp
        { mon with Level = level; MaxHp = newMaxHp; Hp = max 0 (mon.Hp + hpGain) }

    /// Convert a PartyMon to a BattleMon for use in battle (seam for M13/M14).
    /// Move lookup is approximate until M13 wires the full move set.
    let toBattleMon (mon: PartyMon) : BattleMon =
        let species =
            speciesOf mon.SpeciesId
            |> Option.defaultWith (fun () ->
                { Dex = mon.SpeciesId; Name = string mon.SpeciesId
                  Hp = 45; Attack = 45; Defense = 45; Speed = 45; SpAttack = 45; SpDefense = 45
                  Type1 = 0; Type2 = 0
                  CatchRate = 45; BaseExp = 64
                  Item1 = None; Item2 = None; GenderRatio = 255; GrowthRate = 0 })
        let moveSlots =
            mon.Moves
            |> List.choose (fun (moveId, pp) ->
                Moves.tryByIndex moveId |> Option.map (fun move -> move, pp))
            |> List.truncate 4
        let status =
            match mon.Status with
            | status when status.StartsWith("SLP:") ->
                match Int32.TryParse(status.Substring(4)) with
                | true, turns -> Sleep(max 1 (min 7 turns))
                | _ -> Sleep 1
            | "SLP" -> Sleep 1
            | "PSN" -> Poison
            | "BRN" -> Burn
            | "FRZ" -> Freeze
            | "PAR" -> Paralysis
            | _ -> Healthy
        let bm = BattleMon.ofSpeciesWithStats species mon.Level (moveSlots |> List.map fst) mon.Dvs mon.StatExp
        { bm with
            PersistentId = Some mon.Id
            Persistent =
                Some
                    { Species = species
                      Stats = BattleMon.calculateStats species mon.Level mon.Dvs mon.StatExp
                      Moves = moveSlots
                      Exp = mon.Exp
                      StatExp = mon.StatExp
                      Friendship = mon.Friendship }
            Hp = min mon.Hp bm.MaxHp
            Pp = moveSlots |> List.map snd
            HeldItem = mon.HeldItem
            Status = status
            Dvs = mon.Dvs
            Gender = BattleMon.genderFromDvs species mon.Dvs }

    /// Extract individual DVs from the packed int.
    /// Format: bits 15-12 = Atk, 11-8 = Def, 7-4 = Spd, 3-0 = Spc
    let atkDv (dvs: int) = (dvs >>> 12) &&& 0xF
    let defDv (dvs: int) = (dvs >>> 8) &&& 0xF
    let spdDv (dvs: int) = (dvs >>> 4) &&& 0xF
    let spcDv (dvs: int) = dvs &&& 0xF

    /// Derive the Unown letter form (0=A .. 25=Z) from packed DVs.
    /// Source: engine/pokemon/unown_form.asm::GetUnownLetter
    let unownLetter (dvs: int) : int =
        let atk = atkDv dvs
        let def' = defDv dvs
        let spd = spdDv dvs
        let spc = spcDv dvs
        let packed =
            (((atk >>> 1) &&& 3) <<< 6)
            ||| (((def' >>> 1) &&& 3) <<< 4)
            ||| (((spd >>> 1) &&& 3) <<< 2)
            ||| ((spc >>> 1) &&& 3)
        packed / 10

    /// Convert an Unown letter index (0-25) to its ASCII character.
    let unownChar (letterIndex: int) : char =
        char (int 'A' + letterIndex)

    /// Gen-2 shininess check: Atk DV has bit 1 set (2,3,6,7,10,11,14,15)
    /// AND Def, Spd, Spc DVs are all >= 10.
    /// Source: engine/pokemon/search.asm::CheckShininess
    let isShiny (dvs: int) : bool =
        let atk = atkDv dvs
        let def' = defDv dvs
        let spd = spdDv dvs
        let spc = spcDv dvs
        (atk &&& 2 <> 0) && def' >= 10 && spd >= 10 && spc >= 10
