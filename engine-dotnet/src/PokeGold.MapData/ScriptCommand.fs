namespace PokeGold.Game.Overworld.Script

/// The overworld **script command language** — a discriminated union mirroring the
/// disassembly's `*_script` event commands (`engine/overworld/scripting.asm`'s
/// 162-entry `ScriptCommandTable`, with argument layouts from
/// `macros/scripts/events.asm`). This is the high-level re-expression of the
/// bytecode the GSC script VM runs: NPC/sign text, event flags, branching,
/// item/battle/movement/warp.
///
/// This is the M9 *slice* — the ~40 commands the early overworld actually needs.
/// Every other opcode parses to `Unsupported` (name + raw args) so a whole map's
/// scripts still load and the coverage pass (M9.6) can size the next slice.
///
/// Operands that are symbolic in the source (labels, `EVENT_*`/`ENGINE_*`/`VAR_*`
/// names, item/trainer/species/object/map constants, text/movement/song labels)
/// are kept as strings and resolved later by the VM (M9.2) and the integration
/// layer (M9.4). Operands the source always writes as plain numbers (values,
/// quantities, coordinates, comparands) are parsed to `int`. Local label targets
/// (`.Foo`) arrive here already qualified with their enclosing global label.
type ScriptCommand =
    // ---- Control flow ----------------------------------------------------
    /// `scall $00` — call a sub-script (pushes a return address).
    | Scall of target: string
    /// `sjump $03` — unconditional jump.
    | Sjump of target: string
    /// `iffalse $08` — jump if the script-var comparison result is false/zero.
    | Iffalse of target: string
    /// `iftrue $09` — jump if true/nonzero.
    | Iftrue of target: string
    /// `ifequal $06 value, target` — jump if the script var equals `value`.
    | Ifequal of value: int * target: string
    /// `ifnotequal $07 value, target`.
    | Ifnotequal of value: int * target: string
    /// `ifgreater $0a value, target`.
    | Ifgreater of value: int * target: string
    /// `ifless $0b value, target`.
    | Ifless of value: int * target: string

    // ---- Variables -------------------------------------------------------
    /// `setval $15 value` — set the script var.
    | Setval of value: int
    /// `addval $16 value` — add to the script var.
    | Addval of value: int
    /// `readvar $1c VAR_*` — load a game variable into the script var.
    | Readvar of var: string
    /// `writevar $1d VAR_*` — store the script var into a game variable.
    | Writevar of var: string

    // ---- Event flags (the persistent EVENT_* bitset) ---------------------
    /// `checkevent $31 EVENT_*` — script var := flag bit.
    | Checkevent of flag: string
    /// `clearevent $32 EVENT_*`.
    | Clearevent of flag: string
    /// `setevent $33 EVENT_*`.
    | Setevent of flag: string

    // ---- Engine flags (badges, options — a separate ENGINE_* bitset) -----
    /// `checkflag $34 ENGINE_*`.
    | Checkflag of flag: string
    /// `clearflag $35 ENGINE_*`.
    | Clearflag of flag: string
    /// `setflag $36 ENGINE_*`.
    | Setflag of flag: string

    // ---- Map scene state -------------------------------------------------
    /// `checkmapscene $11 MAP` — script var := that map's scene id.
    | Checkmapscene of map: string
    /// `setmapscene $12 MAP, scene`.
    | Setmapscene of map: string * scene: int
    /// `checkscene $13` — script var := this map's scene id.
    | Checkscene
    /// `setscene $14 scene` — set this map's scene id.
    | Setscene of scene: int

    // ---- Items -----------------------------------------------------------
    /// `giveitem $1f ITEM, qty` — silent add to the bag (var := success).
    | Giveitem of item: string * qty: int
    /// `takeitem $20 ITEM, qty`.
    | Takeitem of item: string * qty: int
    /// `checkitem $21 ITEM` — var := has item.
    | Checkitem of item: string
    /// `verbosegiveitem $9d ITEM, qty` — add + auto "received ITEM!" text.
    | Verbosegiveitem of item: string * qty: int

    // ---- Text & UI -------------------------------------------------------
    /// `opentext $47` — open the text box.
    | Opentext
    /// `closetext $49` — close the text box.
    | Closetext
    /// `writetext $4c text` — print text (label resolved by the text seam).
    | Writetext of text: string
    /// `jumptext $52 text` — opentext+writetext+waitbutton+closetext+end.
    | Jumptext of text: string
    /// `jumptextfaceplayer $51 text` — face the player, then `jumptext`.
    | Jumptextfaceplayer of text: string
    /// `waitbutton $53` — wait for A/B.
    | Waitbutton
    /// `promptbutton $54` — wait for A/B showing the prompt cursor.
    | Promptbutton
    /// `yesorno $4e` — yes/no menu (var := chose yes).
    | Yesorno

    // ---- Battle ----------------------------------------------------------
    /// `loadwildmon $5c SPECIES, level`.
    | Loadwildmon of species: string * level: int
    /// `loadtrainer $5d GROUP, id`.
    | Loadtrainer of group: string * id: string
    /// `startbattle $5e` — run the loaded battle (var := result).
    | Startbattle
    /// `reloadmapafterbattle $5f`.
    | Reloadmapafterbattle
    /// `winlosstext $63 win, loss` — set the trainer win/loss text.
    | Winlosstext of win: string * loss: string
    /// `setlasttalked $67 object` — set the active object id.
    | Setlasttalked of obj: string

    // ---- Movement & objects ----------------------------------------------
    /// `applymovement $68 object, movement` — run a movement script on an object.
    | Applymovement of obj: string * movement: string
    /// `faceplayer $6a` — turn the active object toward the player.
    | Faceplayer
    /// `faceobject $6b a, b` — turn object a toward object b.
    | Faceobject of a: string * b: string
    /// `disappear $6d object` — hide an object (and set its hidden bit).
    | Disappear of obj: string
    /// `appear $6e object` — show an object.
    | Appear of obj: string
    /// `turnobject $75 object, facing`.
    | Turnobject of obj: string * facing: string

    // ---- Audio -----------------------------------------------------------
    /// `playmusic $7e song`.
    | Playmusic of song: string
    /// `playsound $84 sound`.
    | Playsound of sound: string
    /// `waitsfx $85` — wait for the current SFX to finish.
    | Waitsfx
    /// `cry $83 species` — play a Pokémon cry.
    | Cry of species: string

    // ---- Map & warp ------------------------------------------------------
    /// `warp $3c MAP, x, y` — warp to a map cell.
    | Warp of map: string * x: int * y: int
    /// `warpfacing $a1 facing, MAP, x, y`.
    | Warpfacing of facing: string * map: string * x: int * y: int
    /// `reloadmap $7a`.
    | Reloadmap
    /// `refreshmap $7b`.
    | Refreshmap

    // ---- Terminators -----------------------------------------------------
    /// `end $90` — return from `scall`, or stop the script.
    | End
    /// `endall $92` — stop all script execution.
    | EndAll

    // ---- Fallback --------------------------------------------------------
    /// Any opcode outside the M9 slice: the source mnemonic + its raw args,
    /// kept verbatim so the map still loads and coverage can be measured.
    | Unsupported of name: string * args: string list

/// A parsed script program for one map file: a flat command stream plus the
/// label → command-index map the VM uses to resolve jumps/calls. Mirrors the
/// audio `Song` shape (a shared command array addressed by index). Labels point
/// at the index of the command that follows them (multiple labels may share an
/// index; a label at the very end points one past the last command).
type ScriptProgram =
    { Commands: ScriptCommand[]
      Labels: Map<string, int> }

module ScriptProgram =

    /// The command sequence starting at `label`, read until (and including) the
    /// first terminator (`End`/`EndAll`) or the end of the stream. Useful for
    /// tests and for tracing a single labelled script without running the VM.
    let blockAt (label: string) (prog: ScriptProgram) : ScriptCommand list =
        match prog.Labels.TryFind label with
        | None -> []
        | Some start ->
            let rec loop i acc =
                if i >= prog.Commands.Length then
                    List.rev acc
                else
                    let cmd = prog.Commands.[i]

                    match cmd with
                    | End
                    | EndAll -> List.rev (cmd :: acc)
                    | _ -> loop (i + 1) (cmd :: acc)

            loop start []
