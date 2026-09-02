import "./styles.css";
import { connectBridge } from "./bridge";
import type { AppViewModel } from "./contracts";
import { developmentFixture } from "./development-fixture";
import { mountApp } from "./render";

const root = document.querySelector<HTMLElement>("#app");
if (!root) throw new Error("Не найден корневой элемент приложения.");

let send: ReturnType<typeof connectBridge>["send"] = () => undefined;
const mounted = mountApp(root, (command) => send(command));
const bridge = connectBridge(
  (model: AppViewModel) => mounted.update(model),
  import.meta.env.DEV ? developmentFixture : undefined,
);
send = bridge.send;
