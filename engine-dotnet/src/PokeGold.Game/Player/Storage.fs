namespace PokeGold.Game.Player

/// A mail message stored in the PC mailbox.
type Mail = { Author: string; Body: string; Species: int }

/// A single PC box: a name and the list of stored Pokémon (up to Storage.monsPerBox).
type Box = { Name: string; Mons: PartyMon list }

/// The full PC storage state: boxes, current box index, PC item stash, and mailbox.
type PcStorage =
    { Boxes: Box array        // length = Storage.numBoxes (14)
      CurrentBox: int
      PcItems: (string * int) list
      Mailbox: Mail list }

/// Constants and the empty initial state for Bill's PC storage.
/// Box deposit/withdraw/move ops are added in M12.3; this is the pure model.
module Storage =

    /// Number of PC boxes — GSC INT version: NUM_BOXES = 14 (constants/pokemon_data_constants.asm).
    [<Literal>]
    let numBoxes = 14

    /// Maximum Pokémon stored per box — GSC: MONS_PER_BOX = 20 (constants/pokemon_data_constants.asm).
    [<Literal>]
    let monsPerBox = 20

    /// Maximum mail items in the mailbox — GSC: MAILBOX_CAPACITY = 10 (constants/item_data_constants.asm).
    [<Literal>]
    let mailboxCapacity = 10

    /// The initial empty PC storage: 14 named boxes ("BOX 1".."BOX 14"), no items, no mail.
    let empty : PcStorage =
        { Boxes = Array.init numBoxes (fun i -> { Name = sprintf "BOX %d" (i + 1); Mons = [] })
          CurrentBox = 0
          PcItems = []
          Mailbox = [] }
