# Items and Shop

The shop mixes persistent inventory items with instant-use purchases.

## Shop rules

- Open the shop with `!shop`, `!shopmenu`, or `!buymenu`.
- Items are sorted by price in the menu.
- Persistent items use live inventory slots.
- Instant items do not use inventory slots.
- The default carry limit for persistent items is `2`.
- You cannot own duplicate persistent items.
- Persistent items are re-applied when the player spawns again while they are still in the live `WarcraftPlayer` inventory.

## Item catalog

| Item | Price | Type | Behavior |
| --- | ---: | --- | --- |
| Boots of Speed | `$2500` | Persistent | Increase speed by `20%`. |
| Sock of Feathers | `$1500` | Persistent | Decrease gravity by `50%`. |
| Ring of Regeneration | `$3000` | Persistent | Regenerate `1 HP` each second. |
| Mask of Death | `$4000` | Persistent | `50%` chance to heal `15%` of weapon damage dealt. |
| Amulet of the Cat | `$4000` | Persistent | Silent footsteps. |
| Dagger of Venom | `$2500` | Persistent | Poison enemies on hit. |
| Money Siphon Scepter | `$3000` | Persistent | Steal `2%` of enemy money on hit. |
| Orb of Frost | `$3500` | Persistent | `33%` chance to slow an enemy on hit. |
| Talisman of Evasion | `$4000` | Persistent | `20%` chance to evade incoming damage. |
| Amulet of Vitality | `$3500` | Persistent | Increase max HP by `50`. |
| Gloves of Wrath | `$3000` | Persistent | Receive an HE grenade every `12s`. |
| Gloves of Cloud | `$3000` | Persistent | Receive a smoke grenade every `12s`. |
| Gloves of Dazzle | `$3000` | Persistent | Receive a flashbang every `12s`. |
| Tome of Experience | `$4000` | Instant | Gain `50 XP` immediately. |
| Tome of Gambling | `$8000` | Instant | Gain a random `50-150 XP`. |
| Weapon Insurance | `$3500` | Instant | Restore purchased weapons after your next death. |
| Blademail | `$10000` | Persistent | `50%` chance to reflect `33%` of incoming weapon damage. |
| Deathbound Bulwark | `$4000` | Persistent | Reduce weapon damage taken by `25%`. |

## Persistent vs instant items

### Persistent items

Persistent items are stored in the live Warcraft player inventory and re-applied after spawn. They are not currently saved to SQLite, so long-term player progression persistence applies to race XP, level, and ability levels, not item inventory. Their passive effects also participate in hurt-event hooks where relevant, such as:

- lifesteal
- venom
- slow on hit
- damage reflection
- damage reduction

### Instant items

Instant items are consumed on purchase and do not occupy an inventory slot:

- Tome of Experience
- Tome of Gambling
- Weapon Insurance

Weapon Insurance is special: it stores a one-time token, snapshots eligible purchased weapons on death, and restores them on the next spawn.

## Shop economy notes

- The player must have enough in-game cash to buy an item.
- The plugin subtracts the price from `InGameMoneyServices`.
- If the item cannot be applied or would violate ownership rules, the purchase is rejected.

## Balance notes

- The strongest long-term defensive items are Blademail, Deathbound Bulwark, and Talisman of Evasion.
- The strongest movement package is Boots of Speed plus Sock of Feathers.
- The grenade gloves add recurring utility, which matters more in long rounds than their purchase price suggests.
- Tome of Gambling is high variance but can fast-track race unlocks on progression-focused servers.
