using System.Collections.Specialized;
using System.Net.Http.Json;
using HMS.Api.IntegrationTests;
using HMS.Contracts.Inventory;
using HMS.Contracts.Restaurant;
using HMS.Desktop.ViewModels;

namespace HMS.Desktop.Tests;

/// <summary>The POS screen behaves like it does in the window, where list selections are cleared on every reload.</summary>
public sealed class RestaurantScreenTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    private static async Task<T> PostAsync<T>(HotelSetup hotel, string relative, object body)
    {
        var response = await hotel.Admin.PostAsJsonAsync(hotel.P(relative), body);
        Assert.True(response.IsSuccessStatusCode, $"{relative}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>What a WPF DataGrid does: a reload clears the selection, and an item that is not in the list cannot stay selected.</summary>
    private static void BindLikeTheWindow(RestaurantPageViewModel pos)
    {
        pos.OpenOrders.CollectionChanged += (_, e) =>
        {
            if (e.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace
                && pos.SelectedOpenOrder is { } selected && !pos.OpenOrders.Contains(selected))
                pos.SelectedOpenOrder = null;
        };
        pos.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(pos.SelectedOpenOrder) && pos.SelectedOpenOrder is { } s && !pos.OpenOrders.Contains(s))
                pos.SelectedOpenOrder = null;
        };
    }

    [Fact]
    public async Task The_open_order_stays_on_screen_while_dishes_are_added()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var store = await PostAsync<StockLocationDto>(hotel, "inventory/locations", new SaveStockLocationRequest("STORE", "Main store"));
        var outlet = await PostAsync<OutletDto>(hotel, "restaurant/outlets", new SaveOutletRequest("REST", "Main Restaurant", "Restaurant", store.Id));
        await PostAsync<TableDto>(hotel, $"restaurant/outlets/{outlet.Id}/tables", new SaveTableRequest("T1", 4));
        await PostAsync<MenuItemDto>(hotel, $"restaurant/outlets/{outlet.Id}/menu", new SaveMenuItemRequest(null, "JOL", "Jollof rice", 3_000, false, "Kitchen", null));
        await PostAsync<MenuItemDto>(hotel, $"restaurant/outlets/{outlet.Id}/menu", new SaveMenuItemRequest(null, "ZOB", "Zobo", 1_000, false, "Bar", null));
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);

        var pos = new RestaurantPageViewModel(api, session, new YesDialogs());
        BindLikeTheWindow(pos);
        await pos.LoadAsync();
        TestDesk.Ok(pos);
        Assert.Null(pos.StatusMessage);

        pos.Table = pos.Tables.Single();
        await pos.NewOrderCommand.ExecuteAsync(null);
        TestDesk.Ok(pos);
        Assert.NotNull(pos.Order);
        Assert.Same(pos.Order, pos.SelectedOpenOrder);
        Assert.Equal("T1", pos.Table?.TableNumber);

        foreach (var dish in pos.Menu.ToList())
        {
            pos.MenuItem = dish;
            await pos.AddItemCommand.ExecuteAsync(null);
            TestDesk.Ok(pos);
            Assert.NotNull(pos.Order);
        }
        Assert.Equal(4_000m, pos.Order!.Total);
        Assert.Equal(4_000m, pos.OpenOrders.Single().Total); // the list shows the new total too

        await pos.SendCommand.ExecuteAsync(null);
        TestDesk.Ok(pos);
        Assert.NotNull(pos.Order);
        Assert.Equal(2, pos.Tickets.Count); // one ticket for the kitchen, one for the bar

        // A walk-in order next to the table order; picking rows switches between them.
        pos.ClearTableCommand.Execute(null);
        await pos.NewOrderCommand.ExecuteAsync(null);
        TestDesk.Ok(pos);
        Assert.Equal(2, pos.OpenOrders.Count);
        Assert.Null(pos.Order!.TableId);
        pos.SelectedOpenOrder = pos.OpenOrders.Single(o => o.TableId is not null);
        Assert.Equal(4_000m, pos.Order!.Total);
    }

    [Fact]
    public async Task An_empty_outlet_list_says_what_to_set_up()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);
        var pos = new RestaurantPageViewModel(api, session, new YesDialogs());
        await pos.LoadAsync();
        TestDesk.Ok(pos);
        Assert.Contains("Setup > Outlets", pos.StatusMessage);
    }

    [Fact]
    public async Task Changing_a_dish_on_the_setup_screen_keeps_its_recipe()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var store = await PostAsync<StockLocationDto>(hotel, "inventory/locations", new SaveStockLocationRequest("STORE", "Main store"));
        var rice = await PostAsync<InventoryItemDto>(hotel, "inventory/items", new SaveInventoryItemRequest("RICE", "Rice", "kg", "Food", 5, "FoodCost"));
        var outlet = await PostAsync<OutletDto>(hotel, "restaurant/outlets", new SaveOutletRequest("REST", "Main Restaurant", "Restaurant", store.Id));
        await PostAsync<MenuItemDto>(hotel, $"restaurant/outlets/{outlet.Id}/menu",
            new SaveMenuItemRequest(null, "JOL", "Jollof rice", 3_000, true, "Kitchen", [new IngredientRequest(rice.Id, 0.5m)]));
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);

        var setup = new SetupPageViewModel(api, session);
        await setup.LoadAsync();
        setup.SelectedEntity = setup.Entities.Single(e => e.Title == "Menu items");
        await setup.WhenLoaded;
        setup.SelectedRow = setup.Rows.Single();
        setup.Form.Single(f => f.Name == "Price").Text = "3500";
        await setup.SaveCommand.ExecuteAsync(null);
        TestDesk.Ok(setup);

        var menu = await hotel.GetAsync<List<MenuItemDto>>($"restaurant/outlets/{outlet.Id}/menu");
        Assert.Equal(3_500m, menu.Single().Price);
        Assert.Equal(0.5m, Assert.Single(menu.Single().Ingredients).Quantity);
    }
}
