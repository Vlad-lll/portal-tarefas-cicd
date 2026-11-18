import { Navigate, Route, Routes } from "react-router-dom";
import RotaProtegida from "./auth/RotaProtegida";
import Login from "./paginas/Login";
import ListaTarefas from "./paginas/ListaTarefas";
import DetalheTarefa from "./paginas/DetalheTarefa";
import NovaTarefa from "./paginas/NovaTarefa";

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route element={<RotaProtegida />}>
        <Route path="/" element={<Navigate to="/tarefas" replace />} />
        <Route path="/tarefas" element={<ListaTarefas />} />
        <Route path="/tarefas/nova" element={<NovaTarefa />} />
        <Route path="/tarefas/:id" element={<DetalheTarefa />} />
      </Route>
      <Route path="*" element={<p>Página não encontrada.</p>} />
    </Routes>
  );
}
