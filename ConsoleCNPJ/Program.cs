using System.Data;
using System.Data.SqlClient;
using System.Net.Http;
using System.Text.Json.Nodes;
using ConsoleCNPJ.Connection;
using Newtonsoft.Json.Linq;

namespace ConsoleCNPJ
{
    class Program
    {
        //Tarefa Assíncrona:
        static async Task Main(string[] args)
        {
            int i = 0;

            using (SqlConnection conn = new SqlConnection(Conexao.StrCon))
            {
                // Abrindo conexão uma vez
                conn.Open();

                // Atualizar validação dentro do loop
                do
                {
                    try
                    {
                        // Recalcular validação:
                        var validaScript = $@"select top 1 CNPJ, UF from empresas where Ins_Estadual is null AND CNPJ IS NOT NULL";
                        using (SqlCommand sqlCommand = new SqlCommand(validaScript, conn))
                        {
                            string? cnpj = null;
                            string? ufBanco = null;

                            using (SqlDataReader reader = sqlCommand.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    cnpj = reader["CNPJ"].ToString();
                                    ufBanco = reader["UF"].ToString();
                                }
                            }

                            if (string.IsNullOrEmpty(cnpj))
                            {
                                Console.WriteLine("Nenhum registro para atualizar.");
                                break;
                            }

                            string url = "https://open.cnpja.com/office/" + cnpj;

                            //Conexão com a API pelo método Http:
                            using (HttpClient client = new HttpClient())
                            {
                                HttpResponseMessage response = await client.GetAsync(url);

                                //Se receber o código 200:
                                if (response.IsSuccessStatusCode)
                                {
                                    //Obter todos os dados da resposta do json:
                                    string jsonResponse = await response.Content.ReadAsStringAsync();
                                    JObject data = JObject.Parse(jsonResponse);

                                    //Atribuir nas variáveis os dados que preciso:
                                    string? cnpjValue = data["taxId"]?.ToString();
                                    string? ufMatriz = data["address"]?["state"]?.ToString();

                                    //Verificar se o Array de Inscrições estaduais existe e contém elementos
                                    var inscricoesEstaduaisArray = data["registrations"] as JArray;

                                    //Se o array for vazio ou nulo
                                    if (inscricoesEstaduaisArray == null || inscricoesEstaduaisArray.Count == 0)
                                    {
                                        //Se IE é nula ou vazia
                                        Console.WriteLine($"Inscrição Estadual não encontrada no CNPJ {cnpjValue}");

                                        //Gravar no banco IE = 0 
                                        using (SqlCommand command = conn.CreateCommand())
                                        {
                                            command.CommandText = "UPDATE empresas SET Ins_Estadual = '0' where CNPJ = @cnpj";
                                            command.Parameters.AddWithValue("@cnpj", cnpjValue);

                                            command.ExecuteNonQuery();
                                        }
                                        await Task.Delay(15000);
                                        continue;
                                    }

                                    //Caso exista IE no Array
                                    foreach (var inscricao in inscricoesEstaduaisArray)
                                    {
                                        string ins_estadualValue = inscricao?["number"]?.ToString();
                                        string uf_ie = inscricao?["state"]?.ToString();
                                        string situacao_ie = inscricao?["enabled"]?.ToString().ToLower() == "true" ? "Ativo" : "Inativo";

                                        //Se Ie for inativo, atualiza IE para 0 no banco
                                        if (situacao_ie == "Inativo")
                                        {
                                            using (SqlCommand command = conn.CreateCommand())
                                            {
                                                command.CommandText = "UPDATE empresas SET Ins_Estadual = '0' where CNPJ = @cnpj and UF = @uf";
                                                command.Parameters.AddWithValue("@cnpj", cnpjValue);
                                                command.Parameters.AddWithValue("uf", uf_ie);

                                                command.ExecuteNonQuery();
                                            }
                                        }

                                        //Se for Ativo, atualizar no banco
                                        if (!string.IsNullOrEmpty(cnpjValue) && !string.IsNullOrEmpty(ins_estadualValue) && situacao_ie == "Ativo" && uf_ie == ufBanco)
                                        {
                                            Console.WriteLine($"Atualizando [{i}] - CNPJ: {cnpjValue} | IE: {ins_estadualValue}|{uf_ie}");

                                            //Atualizar a IE no Banco
                                            try
                                            {
                                                using (SqlCommand command = conn.CreateCommand())
                                                {
                                                    command.CommandText = "UPDATE empresas SET Ins_Estadual = @ie, Situacao = @situacao where CNPJ = @cnpj and UF = @uf";
                                                    command.Parameters.AddWithValue("@ie", ins_estadualValue);
                                                    command.Parameters.AddWithValue("@cnpj", cnpjValue);
                                                    command.Parameters.AddWithValue("@uf", uf_ie);
                                                    command.Parameters.AddWithValue("@situacao", situacao_ie);

                                                    int rowsAffected = command.ExecuteNonQuery();

                                                    if (rowsAffected == 0)
                                                    {
                                                        throw new Exception("Nenhuma linha foi atualizada, realizando Insert!");
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Erro: {ex.Message}. Tentando realizar INSERT.");

                                                using (SqlCommand command = conn.CreateCommand())
                                                {
                                                    command.CommandText = "INSERT INTO empresas (CNPJ, UF, Ins_estadual, Situacao) VALUES (@cnpj, @uf ,@ie, @situacao)";
                                                    command.Parameters.AddWithValue("@ie", ins_estadualValue);
                                                    command.Parameters.AddWithValue("@cnpj", cnpjValue);
                                                    command.Parameters.AddWithValue("@uf", uf_ie);
                                                    command.Parameters.AddWithValue("@situacao", situacao_ie);

                                                    command.ExecuteNonQuery();
                                                }
                                            }

                                            i++;
                                            //Atraso de 11 segundos para a pesquisa (5 por minuto)
                                            await Task.Delay(11000);
                                        }
                                        else
                                        {
                                            try
                                            {
                                                using (SqlCommand command = conn.CreateCommand())
                                                {
                                                    command.CommandText = "UPDATE empresas SET Ins_Estadual = '0' where CNPJ = @cnpj and UF = @uf OR UF = @ufBanco";
                                                    command.Parameters.AddWithValue("@cnpj", cnpjValue);
                                                    command.Parameters.AddWithValue("@uf", ufMatriz);
                                                    command.ExecuteNonQuery();
                                                }

                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"Ajustar UF da Matriz no Banco para o CNPJ {cnpjValue}");
                                                using (SqlCommand command = conn.CreateCommand())
                                                {
                                                    command.CommandText = "UPDATE empresas SET UF = @uf where CNPJ = @cnpj and @ufBanco <> @uf and Ins_Estadual is null";
                                                    command.Parameters.AddWithValue("@cnpj", cnpjValue);
                                                    command.Parameters.AddWithValue("@uf", ufMatriz);
                                                    command.Parameters.AddWithValue("@ufBanco", ufBanco);
                                                    command.ExecuteNonQuery();
                                                }

                                                Console.WriteLine("Atualização realizada.");
                                            }
                                        }
                                    }
                                    continue;
                                }
                                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                                {
                                    //Erro 429:
                                    Console.WriteLine($"Erro ao pesquisar CNPJ: {cnpj}. Código de Status: TooManyRequests. Motivo: {response.ReasonPhrase}");
                                    await Task.Delay(60000);

                                }
                                else
                                {
                                    Console.WriteLine($"Erro ao Pesquisar CNPJ:{cnpj}. Código de status: {response.StatusCode}. Motivo: {response.ReasonPhrase}");

                                    string responseContent = await response.Content.ReadAsStringAsync();
                                    Console.WriteLine($"Resposta completa da API: {responseContent}");

                                    JObject dataResponse = JObject.Parse(responseContent);
                                    string? infoBadRequest = dataResponse["message"]?.ToString();
                                    string? codStatus = dataResponse["code"]?.ToString();

                                    try
                                    {
                                        using (SqlCommand command = conn.CreateCommand())
                                        {
                                            command.CommandText = "UPDATE empresas SET Ins_Estadual = @info where CNPJ = @cnpj";
                                            command.Parameters.AddWithValue("@cnpj", cnpj);
                                            command.Parameters.AddWithValue("@info", infoBadRequest);
                                            command.ExecuteNonQuery();
                                        }
                                        Console.WriteLine($"CNPJ: {cnpj} atualizado no banco.");
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Erro: {ex.Message}");
                                    }

                                }
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }

                } while (true);

                // Fechando a conexão após o loop
                conn.Close();
            }
        }
    }
}
