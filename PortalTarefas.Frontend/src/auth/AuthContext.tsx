import { createContext, useContext, useEffect, useState } from "react";
import type { ReactNode } from "react";
import { entrar, obterSessao, sair } from "../api/http";
import type { Sessao } from "../api/http";

interface Ctx {
  sessao: Sessao | null;
  carregando: boolean;
  login: (email: string, senha: string) => Promise<void>;
  logout: () => Promise<void>;
  tem: (papel: string) => boolean;
}
const AuthCtx = createContext<Ctx | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [sessao, setSessao] = useState<Sessao | null>(null);
  const [carregando, setCarregando] = useState(true);

  // Restaura a sessão a partir do cookie ao abrir ou recarregar
  useEffect(() => {
    obterSessao()
      .then(setSessao)
      .catch(() => setSessao(null))
      .finally(() => setCarregando(false));
  }, []);

  const login = async (email: string, senha: string) => {
    await entrar(email, senha);
    setSessao(await obterSessao());
  };
  const logout = async () => {
    try { await sair(); } finally { setSessao(null); }
  };
  const tem = (papel: string) => !!sessao?.papeis.includes(papel);

  return (
    <AuthCtx.Provider value={{ sessao, carregando, login, logout, tem }}>
      {children}
    </AuthCtx.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const c = useContext(AuthCtx);
  if (!c) throw new Error("AuthProvider ausente");
  return c;
}
