using System.Collections.Generic;
using System.Text;

namespace PangeaSkirmish
{
    /// <summary>
    /// Banco de testes do tutorial (GDD-027 §6): cada lição registra se a mecânica funcionou.
    /// No fim, vira um relatório (tela + arquivo). Lógica pura, testável em EditMode.
    /// </summary>
    public static class TutorialTestLog
    {
        public struct Entry
        {
            public int chapter;
            public string mechanic;
            public TutorialResult result;
            public string detail;
        }

        private static readonly List<Entry> _entries = new List<Entry>();

        public static IReadOnlyList<Entry> Entries => _entries;

        public static void Reset() => _entries.Clear();

        public static void Record(TutorialLesson l)
        {
            if (l == null) return;
            _entries.Add(new Entry
            {
                chapter = l.chapter,
                mechanic = string.IsNullOrEmpty(l.mechanic) ? l.id : l.mechanic,
                result = l.result,
                detail = l.detail ?? ""
            });
        }

        /// <summary>Relatório legível: uma linha por mecânica, com OK/FALHOU/PULADA.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== RELATÓRIO DE TESTES DO TUTORIAL ===");
            int ok = 0, fail = 0, skip = 0;
            foreach (var e in _entries)
            {
                string tag = e.result == TutorialResult.OK ? "OK     "
                           : e.result == TutorialResult.Failed ? "FALHOU "
                           : e.result == TutorialResult.Skipped ? "PULADA "
                           : "PENDENTE";
                if (e.result == TutorialResult.OK) ok++;
                else if (e.result == TutorialResult.Failed) fail++;
                else if (e.result == TutorialResult.Skipped) skip++;
                sb.Append($"[Cap {e.chapter}] {tag} · {e.mechanic}");
                if (!string.IsNullOrEmpty(e.detail)) sb.Append($" — {e.detail}");
                sb.AppendLine();
            }
            sb.AppendLine($"--- {ok} OK · {fail} falhas · {skip} puladas · {_entries.Count} lições ---");
            return sb.ToString();
        }

        /// <summary>JSON simples para dump em arquivo (sem dependência de UnityEngine).</summary>
        public static string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"entries\":[");
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"chapter\":").Append(e.chapter)
                  .Append(",\"mechanic\":\"").Append(Escape(e.mechanic))
                  .Append("\",\"result\":\"").Append(e.result)
                  .Append("\",\"detail\":\"").Append(Escape(e.detail)).Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
        }
    }
}