import { TarefasClient } from "./client/tarefasClient";

const api = new TarefasClient("http://localhost:5001");
const pagina = await api.listarTarefas(1, 5, "titulo", "asc", undefined, undefined);
console.log(pagina.total, pagina.itens);
