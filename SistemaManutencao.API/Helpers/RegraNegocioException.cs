using System.Security.Claims;

namespace SistemaManutencao.API.Helpers;

public sealed class RegraNegocioException(int statusHttp, string mensagem) : Exception(mensagem)
{
    public int StatusHttp { get; } = statusHttp;
}

public static class UsuarioLogado
{
    public static int Id(ClaimsPrincipal usuario)
    {
        var valor = usuario.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(valor, out int id))
            throw new RegraNegocioException(401, "Usuário não autenticado.");
        return id;
    }
}
