import { useState } from "react";
import type { FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ApiError, criarTarefa } from "../api/http";

export default function NovaTarefa() {
  const navegar = useNavigate();
  const [titulo, setTitulo] = useState("");
  const [descricao, setDescricao] = useState("");
  const [erros, setErros] = useState<Record<string, string[]>>({});
  const [geral, setGeral] = useState<string | null>(null);

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setErros({});
    setGeral(null);
    try {
      await criarTarefa(titulo, descricao);
      navegar("/tarefas"); // sem recarregar a página
    } catch (err) {
      if (err instanceof ApiError && err.erros) setErros(err.erros);
      else setGeral((err as Error).message);
    }
  }

  return (
    <form onSubmit={enviar}>
      <Link to="/tarefas">Voltar</Link>
      <h1>Nova tarefa</h1>
      <label>Título
        <input value={titulo} onChange={(e) => setTitulo(e.target.value)} />
      </label>
      {erros.Titulo?.map((m) => <p role="alert" key={m}>{m}</p>)}
      <label>Descrição
        <textarea value={descricao}
          onChange={(e) => setDescricao(e.target.value)} />
      </label>
      {erros.Descricao?.map((m) => <p role="alert" key={m}>{m}</p>)}
      {geral && <p role="alert">{geral}</p>}
      <button type="submit">Salvar</button>
    </form>
  );
}
