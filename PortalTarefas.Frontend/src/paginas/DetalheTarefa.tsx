import { useEffect, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ApiError, excluirTarefa, obterTarefa } from "../api/http";
import { useAuth } from "../auth/AuthContext";
import type { Detalhe } from "../api/http";

export default function DetalheTarefa() {
  const { id = "" } = useParams();
  const { tem } = useAuth();
  const navegar = useNavigate();
  const [tarefa, setTarefa] = useState<Detalhe | null>(null);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    const ctrl = new AbortController();
    setTarefa(null);
    setErro(null);
    obterTarefa(id, ctrl.signal)
      .then(setTarefa)
      .catch((e) => {
        if (e.name === "AbortError") return;
        setErro(e instanceof ApiError && e.status === 404
          ? "Tarefa não encontrada." : (e as Error).message);
      });
    return () => ctrl.abort();
  }, [id]);

  // O botão é só conveniência: o servidor valida o papel (403)
  async function excluir() {
    try {
      await excluirTarefa(id);
      navegar("/tarefas");
    } catch (e) {
      setErro((e as Error).message);
    }
  }

  return (
    <section>
      <Link to="/tarefas">Voltar</Link>
      {erro && <p role="alert">{erro}</p>}
      {!erro && !tarefa && <p>Carregando...</p>}
      {tarefa && (
        <>
          <h1>{tarefa.titulo}</h1>
          <p>{tarefa.descricao ?? "Sem descrição."}</p>
          <p>{tarefa.concluida ? "Concluída" : "Aberta"}</p>
          {tem("Administrador") && <button onClick={excluir}>Excluir</button>}
        </>
      )}
    </section>
  );
}
