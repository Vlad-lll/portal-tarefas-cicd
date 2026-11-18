using Xunit;

public class ConfigTests
{
    static string Raiz()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Dockerfile")))
            d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void AppsettingsNaoTemSenha()
    {
        var t = File.ReadAllText(Path.Combine(Raiz(), "PortalTarefas", "appsettings.json"));
        Assert.DoesNotContain("password=", t, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pwd=", t, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DockerfileRodaSemRootESemSegredos()
    {
        var t = File.ReadAllText(Path.Combine(Raiz(), "Dockerfile"));
        Assert.Contains("USER ", t);
        Assert.DoesNotContain("password", t, StringComparison.OrdinalIgnoreCase);
    }
}
