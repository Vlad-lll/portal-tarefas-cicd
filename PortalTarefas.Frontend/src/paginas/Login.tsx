import { useState } from "react";
import type { FormEvent } from "react";
import { Navigate, useLocation, useNavigate } from "react-router-dom";
import { ApiError } from "../api/http";
import { useAuth } from "../auth/AuthContext";

export default function Login() {
  const { sessao, login } = useAuth();
  const navegar = useNavigate();
  const local = useLocation();
  const destino = (local.state as { de?: string } | null)?.de ?? "/tarefas";
  const [email, setEmail] = useState("");
  const [senha, setSenha] = useState("");
  const [erro, setErro] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  if (sessao) return <Navigate to={destino} replace />;

  async function enviar(e: FormEvent) {
    e.preventDefault();
    setErro(null);
    setEnviando(true);
    try {
      await login(email, senha);
      navegar(destino, { replace: true });
    } catch (err) {
      // Mensagem única: não revela se a conta existe
      setErro(err instanceof ApiError && err.status === 401
        ? "E-mail ou senha inválidos." : (err as Error).message);
    } finally {
      setEnviando(false);
    }
  }

  return (
    <form onSubmit={enviar}>
      <h1>Entrar</h1>
      <label>E-mail
        <input type="email" value={email} autoComplete="username"
          onChange={(e) => setEmail(e.target.value)} />
      </label>
      <label>Senha
        <input type="password" value={senha} autoComplete="current-password"
          onChange={(e) => setSenha(e.target.value)} />
      </label>
      {erro && <p role="alert">{erro}</p>}
      <button type="submit" disabled={enviando}>Entrar</button>
    </form>
  );
}
