using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Utils;
using System.Linq;
using WarcraftPlugin.Core;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Items;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Menu.WarcraftMenu;

internal static class ShopMenu
{
    internal static void Show(WarcraftPlayer wcPlayer)
    {
        var plugin = WarcraftPlugin.Instance;
        var localizer = wcPlayer.GetLocalizer();
        var builder = MenuBuilder.Create($"<font color='lightgrey' class='{FontSizes.FontSizeM}'>" + localizer["menu.shop"] + "</font>", 4);

        builder.AddOptions(
            Shop.Items.OrderBy(x => x.Price),
            item => $"<font color='{item.Color.ToHex()}' class='{FontSizes.FontSizeSm}'>{item.LocalizedName}</font><font class='{FontSizes.FontSizeSm}'> - </font><font color='lightgreen' class='{FontSizes.FontSizeSm}'>${item.Price}</font>",
            item => $"<font color='#D3D3D3' class='{FontSizes.FontSizeS}'>{item.LocalizedDescription}</font>",
            (player, option, itemPrototype) =>
            {
                var currentWcPlayer = player.GetWarcraftPlayer();
                if (currentWcPlayer == null)
                {
                    player.PlayLocalSound("sounds/ui/menu_invalid.vsnd");
                    player.PrintToChat($" {ChatColors.Red}Unable to access shop: player data not loaded.");
                    return;
                }

                var itemInstance = itemPrototype.Clone();

                if (!itemInstance.IsInstant)
                {
                    var maxItems = WarcraftPlugin.Instance.Config.MaxInventoryItems;
                    if (maxItems < 1) maxItems = 1;
                    if (currentWcPlayer.Items.Count >= maxItems)
                    {
                    player.PlayLocalSound("sounds/ui/menu_invalid.vsnd");
                    player.PrintToChat($" {localizer["menu.shop.carry_limit", maxItems]}");
                    return;
                }

                if (currentWcPlayer.Items.Any(inv => inv.GetType() == itemInstance.GetType()))
                {
                    player.PlayLocalSound("sounds/ui/menu_invalid.vsnd");
                    player.PrintToChat($" {localizer["menu.shop.already_owned"]}");
                    return;
                }
                }

                try
                {
                    var moneyServices = player.InGameMoneyServices;
                    if (moneyServices == null || moneyServices.Account < itemInstance.Price)
                    {
                    player.PlayLocalSound("sounds/ui/menu_invalid.vsnd");
                    player.PrintToChat($" {localizer["menu.shop.not_enough_money"]}");
                    return;
                    }

                    moneyServices.Account -= itemInstance.Price;
                    Utilities.SetStateChanged(player,
                        "CCSPlayerController", "m_pInGameMoneyServices");
                }
                catch
                {
                    player.PlayLocalSound("sounds/ui/menu_invalid.vsnd");
                    player.PrintToChat($" {ChatColors.Red}Unable to access shop funds right now.");
                    return;
                }

                var applied = false;
                if (itemInstance.IsInstant)
                {
                    itemInstance.Apply(player);
                    applied = true;
                }
                else if (currentWcPlayer.AddItem(itemInstance))
                {
                    itemInstance.Apply(player);
                    applied = true;
                }

                if (!applied)
                {
                    player.PrintToChat($" {localizer["menu.shop.already_owned"]}");
                    player.PlayLocalSound("sounds/ui/menu_invalid.vsnd");
                    return;
                }

                player.PlayLocalSound("sounds/buttons/button9.vsnd");
                player.PrintToChat($" {localizer["menu.shop.bought", itemInstance.LocalizedName]}");
            });

        MenuManager.OpenMainMenu(wcPlayer.Player, builder.Build());
    }
}
