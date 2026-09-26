using DIYB.Cli;
using Xunit;

namespace DIYB.Core.Tests;

public class CliArgumentsTests
{
    [Fact]
    public void Separe_la_commande_des_operandes()
    {
        var arguments = CliArguments.Parse(new[] { "startup", "off", "10011c676a", "10011b6176" });

        Assert.Equal("startup", arguments.Command);
        Assert.Equal(new[] { "off", "10011c676a", "10011b6176" }, arguments.Operands);
    }

    [Fact]
    public void Accepte_les_deux_formes_d_option()
    {
        Assert.Equal(20, CliArguments.Parse(new[] { "list", "--wait", "20" }).Int("wait", 6));
        Assert.Equal(20, CliArguments.Parse(new[] { "list", "--wait=20" }).Int("wait", 6));
    }

    [Fact]
    public void Une_option_sans_valeur_vaut_vrai()
    {
        var arguments = CliArguments.Parse(new[] { "list", "--json", "--all" });

        Assert.True(arguments.Flag("json"));
        Assert.True(arguments.Flag("all"));
        Assert.False(arguments.Flag("verbose"));
    }

    [Fact]
    public void Une_option_suivie_d_une_autre_reste_booleenne()
    {
        var arguments = CliArguments.Parse(new[] { "flash", "--force-signal", "--file", "fw.bin" });

        Assert.True(arguments.Flag("force-signal"));
        Assert.Equal("fw.bin", arguments.Value("file"));
    }

    [Fact]
    public void Une_valeur_negative_explicite_desactive_l_option()
    {
        Assert.False(CliArguments.Parse(new[] { "list", "--json=false" }).Flag("json"));
        Assert.False(CliArguments.Parse(new[] { "list", "--json=0" }).Flag("json"));
    }

    [Fact]
    public void Reconnait_les_abreviations_d_aide()
    {
        Assert.True(CliArguments.Parse(new[] { "-h" }).Has("help"));
        Assert.True(CliArguments.Parse(new[] { "list", "-?" }).Has("help"));
        Assert.True(CliArguments.Parse(new[] { "--help" }).Has("help"));
    }

    [Fact]
    public void Une_ligne_vide_ne_porte_aucune_commande()
    {
        var arguments = CliArguments.Parse(Array.Empty<string>());

        Assert.Equal(string.Empty, arguments.Command);
        Assert.Empty(arguments.Operands);
    }

    [Fact]
    public void Une_valeur_manquante_retombe_sur_le_defaut()
    {
        var arguments = CliArguments.Parse(new[] { "pulse", "on", "--width" });

        Assert.Equal(500, arguments.Int("width", 500));
        Assert.Null(arguments.Value("width"));
    }
}
