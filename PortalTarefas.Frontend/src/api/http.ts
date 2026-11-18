// Único ponto de acesso HTTP da SPA
const BASE = import.meta.env.VITE_API_URL ?? "/api";

export interface Resumo { id: number; titulo: string; concluida: boolean }
export interface Detalhe extends Resumo {
  descricao: string | null; criadaEm: string; rowVersion: string;
}
export interface Pagina<T> {
  itens: T[]; pagina: number; tamanho: number; total: number;
}

// Erro normalizado: rede (status 0), HTTP ou Problem Details
export class ApiError extends Error {
  status: number;
  erros?: Record<string, string[]>;
  constructor(status: number, mensagem: string,
              erros?: Record<string, string[]>) {
    super(mensagem);
    this.status = status;
    this.erros = erros;
  }
}

async function requisitar<T>(caminho: string, init?: RequestInit) {
  let resp: Response;
  try {
    resp = await fetch(`${BASE}${caminho}`, init);
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError(0, "Sem conexão com o servidor.");
  }
  if (!resp.ok) {
    const p = await resp.json().catch(() => null);
    const padrao: Record<number, string> = {
      401: "Sessão ausente ou expirada. Entre novamente.",
      403: "Você não tem permissão para esta ação.",
    };
    throw new ApiError(resp.status,
      padrao[resp.status] ?? p?.detail ?? p?.title ?? `Erro ${resp.status}`,
      p?.errors);
  }
  return (resp.status === 204 ? undefined : await resp.json()) as T;
}

export const listarTarefas = (q: URLSearchParams, signal?: AbortSignal) =>
  requisitar<Pagina<Resumo>>(`/tarefas?${q}`, { signal });

export const obterTarefa = (id: string, signal?: AbortSignal) =>
  requisitar<Detalhe>(`/tarefas/${id}`, { signal });

export const criarTarefa = (titulo: string, descricao: string) =>
  requisitar<Detalhe>("/tarefas", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ titulo, descricao: descricao || null }),
  });

// Sessão (cookie HttpOnly: o JavaScript nunca lê o cookie)
export interface Sessao {
  email: string; papeis: string[]; permissoes: string[];
}
const JSON_H = { "Content-Type": "application/json" };

export const entrar = (email: string, senha: string) =>
  requisitar<void>("/auth/login", {
    method: "POST", headers: JSON_H, body: JSON.stringify({ email, senha }),
  });
export const sair = () =>
  requisitar<void>("/auth/logout", { method: "POST" });
export const obterSessao = () => requisitar<Sessao>("/auth/me");
export const excluirTarefa = (id: string) =>
  requisitar<void>(`/tarefas/${id}`, { method: "DELETE" });
