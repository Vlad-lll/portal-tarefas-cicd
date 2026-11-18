import { Navigate, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "./AuthContext";

export default function RotaProtegida() {
  const { sessao, carregando, logout } = useAuth();
  const local = useLocation();
  const navegar = useNavigate();

  if (carregando) return <p role="status">Carregando sessão...</p>;
  // Guarda a URL pretendida para voltar depois do login
  if (!sessao) {
    return <Navigate to="/login" replace
      state={{ de: local.pathname + local.search }} />;
  }
  return (
    <>
      <header>
        <span>{sessao.email} ({sessao.papeis.join(", ")}) </span>
        <button onClick={async () => { await logout(); navegar("/login"); }}>
          Sair
        </button>
      </header>
      <Outlet />
    </>
  );
}
