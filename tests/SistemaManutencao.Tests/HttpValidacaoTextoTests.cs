using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using SistemaManutencao.API.DTOs;
using Xunit;

namespace SistemaManutencao.Tests;

[Collection(ApiHttpFixture.NomeCollection)]
public sealed class HttpValidacaoTextoTests(ApiHttpFixture api)
{
    [Theory]
    [InlineData(typeof(CriarUsuarioDto), "Nome", 2)]
    [InlineData(typeof(AtualizarUsuarioDto), "Nome", 2)]
    [InlineData(typeof(SetorDto), "Nome", 2)]
    [InlineData(typeof(EquipamentoDto), "Nome", 2)]
    [InlineData(typeof(CriarOcorrenciaDto), "Descricao", 5)]
    [InlineData(typeof(PlanoManutencaoDto), "Descricao", 5)]
    [InlineData(typeof(RegistrarManutencaoDto), "Diagnostico", 5)]
    [InlineData(typeof(RegistrarManutencaoDto), "ServicoRealizado", 5)]
    public void Minimo_considera_texto_sem_espacos_nas_extremidades(Type tipo, string campo, int minimo)
    {
        var dto = Activator.CreateInstance(tipo)!;
        var contexto = new ValidationContext(dto) { MemberName = campo };
        Assert.False(Validator.TryValidateProperty(" " + new string('a', minimo - 1) + " ", contexto, []));
        Assert.True(Validator.TryValidateProperty(" " + new string('a', minimo) + " ", contexto, []));
        Assert.False(Validator.TryValidateProperty(new string(' ', minimo), contexto, []));
    }

    [Fact]
    public async Task Setor_rejeita_atualizacao_curta_sem_alterar_nome_e_aceita_minimo_com_espacos()
    {
        var admin = await api.AdminAsync();
        var id = await api.CriarSetorAsync("Setor regressao Trim");
        var invalida = await admin.PutAsJsonAsync($"/api/setores/{id}", new { nome = " A " });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
        var setores = await (await admin.GetAsync("/api/setores")).CorpoAsync();
        Assert.Equal("Setor regressao Trim", setores.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id).GetProperty("nome").GetString());
        var valida = await admin.PutAsJsonAsync($"/api/setores/{id}", new { nome = " XY " });
        valida.EnsureSuccessStatusCode();
        setores = await (await admin.GetAsync("/api/setores")).CorpoAsync();
        Assert.Equal("XY", setores.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id).GetProperty("nome").GetString());
    }

    [Fact]
    public async Task Registro_invalido_nao_sobrescreve_manutencao_existente()
    {
        var admin = await api.AdminAsync();
        var tecnico = await api.TecnicoAsync();
        var (_, _, ocorrenciaId) = await api.CriarCenarioOcorrenciaAsync("trim-manutencao", "Falha para regressao Trim");
        var criada = await admin.PostAsJsonAsync("/api/ordens-servico", new { tipoManutencao = "Corretiva", ocorrenciaId });
        criada.EnsureSuccessStatusCode();
        var id = (await criada.CorpoAsync()).GetProperty("id").GetInt32();
        (await tecnico.PostAsync($"/api/ordens-servico/{id}/assumir", null)).EnsureSuccessStatusCode();
        (await tecnico.PostAsync($"/api/ordens-servico/{id}/iniciar", null)).EnsureSuccessStatusCode();
        (await tecnico.PutAsJsonAsync($"/api/ordens-servico/{id}/manutencao",
            new { diagnostico = " Falha ", servicoRealizado = " Troca ", observacoes = "Preservar" })).EnsureSuccessStatusCode();
        foreach (var texto in new[] { new { diagnostico = " abcd ", servicoRealizado = "Troca" }, new { diagnostico = "Falha", servicoRealizado = " abcd " } })
        {
            var resposta = await tecnico.PutAsJsonAsync($"/api/ordens-servico/{id}/manutencao", texto);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }
        var os = await (await admin.GetAsync($"/api/ordens-servico/{id}")).CorpoAsync();
        var manutencao = os.GetProperty("manutencao");
        Assert.Equal("Falha", manutencao.GetProperty("diagnostico").GetString());
        Assert.Equal("Troca", manutencao.GetProperty("servicoRealizado").GetString());
        Assert.Equal("Preservar", manutencao.GetProperty("observacoes").GetString());
        Assert.Equal("EmAtendimento", os.GetProperty("status").GetString());
    }
}
