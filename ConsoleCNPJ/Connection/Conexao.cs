using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleCNPJ.Connection
{
    internal class Conexao
    {
        private static string server = "Seu server";
        private static string dataBase = "Seu banco";
        private static string user = "Seu Usuario";
        private static string password = "Sua Senha";

        public static string StrCon
        {
            get
            {
                 return "Data Source=" + server + "; Integrated Security=False; Initial Catalog=" + dataBase + "; User ID=" + user + "; Password=" + password;
            }
        }
    }
}
