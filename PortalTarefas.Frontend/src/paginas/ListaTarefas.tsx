import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ApiError, listarTarefas } from "../api/http";
import { useAuth } from "../auth/AuthContext";
import type { Pagina, Resumo } from "../api/http";

type Estado =
  | { tipo: "idle" }
  | { tipo: "carregando" }
  | { tipo: "vazio" }
  | { tipo: "erro"; mensagem: string }
  | { tipo: "sucesso"; dados: Pagina<Resumo> };

export default function ListaTarefas() {
  const { tem } = useAuth();
  const [params, setParams] = useSearchParams();
  const [estado, setEstado] = useState<Estado>({ tipo: "idle" });
  const [tentativa, setTentativa] = useState(0);
  const query = params.toString();

  useEffect(() => {
    const ctrl = new AbortController(); // ignora resposta obsoleta
    const q = new URLSearchParams(query);
    if (!q.has("tamanho")) q.set("tamanho", "5");
    setEstado({ tipo: "carregando" });
    listarTarefas(q, ctrl.signal)
      .then((d) => setEstado(d.itens.length
        ? { tipo: "sucesso", dados: d } : { tipo: "vazio" }))
      .catch((e) => {
        if (e.name === "AbortError") return;
        setEstado({ tipo: "erro", mensagem:
          e instanceof ApiError ? e.message : "Erro inesperado." });
      });
    return () => ctrl.abort();
  }, [query, tentativa]);

  // Filtros e página vivem na URL (?busca=...&pagina=2)
  function alterar(campo: string, valor: string, voltarPagina = true) {
    const p = new URLSearchParams(params);
    if (valor) p.set(campo, valor); else p.delete(campo);
    if (voltarPagina) p.set("pagina", "1");
    setParams(p);
  }
  const pagina = Number(params.get("pagina") ?? 1);
  const fim = estado.tipo === "sucesso" &&
    pagina * estado.dados.tamanho >= estado.dados.total;

  return (
    <section>
      <h1>Tarefas</h1>
      <input placeholder="Buscar" defaultValue={params.get("busca") ?? ""}
        onChange={(e) => alterar("busca", e.target.value)} />
      <select value={params.get("concluida") ?? ""}
        onChange={(e) => alterar("concluida", e.target.value)}>
        <option value="">Todas</option>
        <option value="false">Abertas</option>
        <option value="true">Concluídas</option>
      </select>
      <select value={params.get("ordenarPor") ?? "criadaEm"}
        onChange={(e) => alterar("ordenarPor", e.target.value)}>
        <option value="criadaEm">Data</option>
        <option value="titulo">Título</option>
      </select>
      {(tem("Usuario") || tem("Administrador")) && (
        <Link to="/tarefas/nova">Nova tarefa</Link>
      )}

      <div role="status" aria-live="polite">
        {estado.tipo === "carregando" && <p>Carregando...</p>}
        {estado.tipo === "vazio" && <p>Nenhuma tarefa encontrada.</p>}
        {estado.tipo === "erro" && (
          <p role="alert">{estado.mensagem}{" "}
            <button onClick={() => setTentativa((t) => t + 1)}>
              Tentar de novo
            </button>
          </p>
        )}
      </div>

      {estado.tipo === "sucesso" && (
        <>
          <ul>
            {estado.dados.itens.map((t) => (
              <li key={t.id}>
                <Link to={`/tarefas/${t.id}`}>{t.titulo}</Link>
                {t.concluida ? " (concluída)" : ""}
              </li>
            ))}
          </ul>
          <button disabled={pagina <= 1}
            onClick={() => alterar("pagina", String(pagina - 1), false)}>
            Anterior
          </button>
          <span> Página {pagina} </span>
          <button disabled={fim}
            onClick={() => alterar("pagina", String(pagina + 1), false)}>
            Próxima
          </button>
        </>
      )}
    </section>
  );
}
