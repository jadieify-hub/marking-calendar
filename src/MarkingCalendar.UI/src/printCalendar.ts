import type { CalendarEventViewModel } from "./contracts";

export function createPrintCalendar(
  events: ReadonlyArray<CalendarEventViewModel>, updatedAt: string, filters: string,
): HTMLElement {
  const sheet = document.createElement("section");
  sheet.className = "print-calendar";
  const heading = document.createElement("h1");
  heading.textContent = "Календарь маркировки";
  const metadata = document.createElement("p");
  metadata.textContent = `Данные от ${updatedAt} · Событий: ${events.length}`;
  const selection = document.createElement("p");
  selection.textContent = filters;
  sheet.append(heading, metadata, selection);
  const table = document.createElement("table");
  const head = table.createTHead().insertRow();
  for (const label of ["Срок", "Товарная группа", "Тип события", "Событие"]) {
    const cell = document.createElement("th");
    cell.scope = "col";
    cell.textContent = label;
    head.append(cell);
  }
  const body = table.createTBody();
  for (const event of events) {
    const row = body.insertRow();
    const dates = event.start && event.end ? `${date(event.start)} – ${date(event.end)}`
      : event.start ? `с ${date(event.start)}` : event.end ? `до ${date(event.end)}` : event.period;
    for (const value of [dates, event.group, event.typeLabel]) row.insertCell().textContent = value;
    const detail = row.insertCell();
    const stage = document.createElement("strong");
    stage.textContent = event.stage;
    detail.append(stage);
    if (event.description && event.description !== event.stage) {
      const description = document.createElement("p");
      description.textContent = event.description;
      detail.append(description);
    }
  }
  const footer = document.createElement("p");
  footer.className = "print-source";
  footer.textContent = "Источник: честныйзнак.рф. Календарь составлен по выбранным фильтрам. Приложение не является официальным продуктом оператора маркировки.";
  sheet.append(table, footer);
  return sheet;
}

function date(iso: string): string {
  return iso.slice(0, 10).split("-").reverse().join(".");
}
