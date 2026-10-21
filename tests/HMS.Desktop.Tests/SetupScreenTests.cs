using System.Reflection;
using HMS.Api.IntegrationTests;
using HMS.Desktop.ViewModels;
using HMS.Desktop.ViewModels.Setup;

namespace HMS.Desktop.Tests;

/// <summary>The Setup screen against the in-memory API: every kind of record can be added and then changed.</summary>
public sealed class SetupScreenTests(HmsApiFactory factory) : IClassFixture<HmsApiFactory>
{
    [Fact]
    public void Every_reference_to_another_record_has_a_pick_list()
    {
        foreach (var entity in SetupCatalog.All)
            foreach (var type in new[] { entity.CreateType, entity.UpdateType }.OfType<Type>())
                foreach (var p in SetupEntity.Constructor(type).GetParameters())
                {
                    var name = char.ToUpperInvariant(p.Name![0]) + p.Name[1..];
                    var underlying = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
                    if (underlying != typeof(Guid) || name == "ConcurrencyStamp") continue;
                    Assert.True(entity.Fields.TryGetValue(name, out var spec) && (spec.Lookup is not null || spec.Hidden),
                        $"{entity.Title}: {name} needs a pick list.");
                }
    }

    [Fact]
    public async Task Every_kind_of_record_can_be_added_and_changed()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);
        var setup = new SetupPageViewModel(api, session);
        await setup.LoadAsync();
        TestDesk.Ok(setup);
        var n = 0;

        foreach (var entity in setup.Entities)
        {
            setup.SelectedEntity = entity;
            await setup.WhenLoaded;
            Assert.True(setup.ErrorMessage is null, $"{entity.Title}: {setup.ErrorMessage}");
            var before = setup.Rows.Count;

            if (entity.CanCreate)
            {
                Assert.True(setup.NewCommand.CanExecute(null), $"{entity.Title}: New is disabled.");
                setup.NewCommand.Execute(null);
                foreach (var field in setup.Form) Fill(field, ++n);
                await setup.SaveCommand.ExecuteAsync(null);
                Assert.True(setup.ErrorMessage is null, $"{entity.Title} (new): {setup.ErrorMessage}");
                Assert.False(setup.IsFormOpen);
                // Templates save a new version and posting accounts replace the mapping for a purpose, so only those can keep the count.
                if (!entity.SaveCreatesVersion && !entity.CreateWithPut)
                    Assert.True(before + 1 == setup.Rows.Count, $"{entity.Title}: expected {before + 1} rows, found {setup.Rows.Count}.");
            }

            if (entity.CanEdit && setup.Rows.Count > 0)
            {
                setup.SelectedRow = setup.Rows[^1];
                Assert.True(setup.IsFormOpen, $"{entity.Title}: selecting a row does not open it.");
                var name = setup.Form.FirstOrDefault(f => f.Name is "Name" or "Title" or "Description" && f.IsTextInput);
                if (name is not null) name.Text += " (changed)";
                await setup.SaveCommand.ExecuteAsync(null);
                Assert.True(setup.ErrorMessage is null, $"{entity.Title} (change): {setup.ErrorMessage}");
                if (name is not null) Assert.Contains(setup.Rows, r => r.Cells.Any(c => c.EndsWith("(changed)", StringComparison.Ordinal)));
            }
        }
    }

    [Fact]
    public async Task Missing_and_badly_typed_values_are_explained_before_anything_is_sent()
    {
        var hotel = await HotelSetup.CreateAsync(factory, roomCount: 1);
        var (api, session) = await TestDesk.SignInAsync(factory, hotel);
        var setup = new SetupPageViewModel(api, session);
        await setup.LoadAsync();
        setup.SelectedEntity = setup.Entities.Single(e => e.Title == "Work shifts");
        await setup.WhenLoaded;
        setup.NewCommand.Execute(null);

        await setup.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Enter code.", setup.ErrorMessage);

        setup.Form.Single(f => f.Name == "Code").Text = "MORN";
        setup.Form.Single(f => f.Name == "Name").Text = "Morning";
        setup.Form.Single(f => f.Name == "StartLocal").Text = "7am";
        setup.Form.Single(f => f.Name == "EndLocal").Text = "15:00";
        await setup.SaveCommand.ExecuteAsync(null);
        Assert.Contains("must be a time", setup.ErrorMessage);

        setup.Form.Single(f => f.Name == "StartLocal").Text = "07:00";
        await setup.SaveCommand.ExecuteAsync(null);
        TestDesk.Ok(setup);
        var row = Assert.Single(setup.Rows);
        Assert.Contains("07:00", row.Cells);
        Assert.Contains("7.5", string.Join(" ", row.Cells)); // 8 hours less the 30-minute break
    }

    private static void Fill(FormField field, int n)
    {
        switch (field.Kind)
        {
            case FieldKind.Text or FieldKind.LongText:
                if (field.IsOptional && field.Name is not ("Email" or "Phone")) return;
                field.Text = field.Name switch
                {
                    "Email" => $"setup{n}@hotel.ng",
                    "Phone" => "08031234567",
                    "Body" => "Dear {{GuestName}}, thank you.",
                    _ when field.Name.EndsWith("Code", StringComparison.Ordinal) || field.Name is "Code" or "Sku" or "RoomNumber" or "TableNumber" or "EmployeeNumber" => $"C{n}",
                    _ => $"{field.Label} {n}",
                };
                break;
            case FieldKind.Number:
                field.Text ??= "10";
                break;
            case FieldKind.WholeNumber:
                field.Text ??= "2";
                break;
            case FieldKind.Time:
                field.Text ??= "07:00";
                break;
            case FieldKind.Choice or FieldKind.Lookup:
                if (field.Selected is null && !(field.AllowsOwnValue && field.Text is not null))
                    field.Selected = field.IsOptional ? field.Options.FirstOrDefault() : field.Options.FirstOrDefault(o => o.Value is not null);
                break;
        }
    }
}
