type ProductGroupIconName =
  | "apparel"
  | "book"
  | "cable"
  | "care"
  | "construction"
  | "drink"
  | "electronics"
  | "food"
  | "health"
  | "home"
  | "mobility"
  | "package"
  | "pet"
  | "shoe"
  | "tobacco"
  | "toy"
  | "vehicle";

const SVG_NAMESPACE = "http://www.w3.org/2000/svg";
const ICON_PATHS: Record<ProductGroupIconName, ReadonlyArray<string>> = {
  apparel: ["M8 4 4.5 6 2 10l3 1.5V20h14v-8.5l3-1.5-2.5-4L16 4a4 4 0 0 1-8 0Z"],
  book: ["M4 5.5A2.5 2.5 0 0 1 6.5 3H11v16H6.5A2.5 2.5 0 0 0 4 21.5Z", "M20 5.5A2.5 2.5 0 0 0 17.5 3H13v16h4.5a2.5 2.5 0 0 1 2.5 2.5Z"],
  cable: ["M7 7a3 3 0 1 0 0 6h3a4 4 0 0 1 0 8", "M17 3v4", "M14 4h6", "M17 13a3 3 0 1 0 0-6"],
  care: ["M12 3s5 5.3 5 10a5 5 0 0 1-10 0c0-4.7 5-10 5-10Z", "m18.5 3 .6 1.4 1.4.6-1.4.6-.6 1.4-.6-1.4-1.4-.6 1.4-.6Z"],
  construction: ["M4 7h16v12H4Z", "M4 12h16", "M9 7v5", "M15 12v7"],
  drink: ["M9 3h6", "M10 3v5l-2 3v9h8v-9l-2-3V3", "M8 13h8"],
  electronics: ["M4 5h16v12H4Z", "M9 21h6", "M12 17v4", "M8 9h8"],
  food: ["M5 8h14l-1 12H6Z", "M8 8a4 4 0 0 1 8 0", "M9 13h6"],
  health: ["M9 3h6v6h6v6h-6v6H9v-6H3V9h6Z"],
  home: ["m3 11 9-8 9 8", "M5 10v11h14V10", "M9 21v-7h6v7"],
  mobility: ["M10 5a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z", "M10 7v7h6l3 6", "M10 10H6", "M9 21a6 6 0 1 1 0-12"],
  package: ["m4 7 8-4 8 4-8 4Z", "M4 7v10l8 4 8-4V7", "M12 11v10"],
  pet: ["M8.5 11.5c-3 1.5-4 5-2 7 1.7 1.7 3.8.4 5.5.4s3.8 1.3 5.5-.4c2-2 1-5.5-2-7-2.2-1.1-4.8-1.1-7 0Z", "M5.5 9A2 2 0 1 0 5 5a2 2 0 0 0 .5 4Z", "M10 7a2 2 0 1 0 0-4 2 2 0 0 0 0 4Z", "M18.5 9A2 2 0 1 1 19 5a2 2 0 0 1-.5 4Z", "M14 7a2 2 0 1 1 0-4 2 2 0 0 1 0 4Z"],
  shoe: ["M4 14c4 0 7-2 8-7l3 5 5 2v4H4Z", "M13 11h3"],
  tobacco: ["M5 19c8 0 13-6 14-15-9 1-15 6-14 15Z", "M5 19c3-5 7-8 12-11"],
  toy: ["M7 3h4v5H7a2 2 0 1 0-4 0V4a1 1 0 0 1 1-1Z", "M13 3h7a1 1 0 0 1 1 1v7h-5a2 2 0 1 1-3 0Z", "M3 13h5a2 2 0 1 1 0 3v5H4a1 1 0 0 1-1-1Z", "M10 21v-5a2 2 0 1 1 3 0v5h7a1 1 0 0 0 1-1v-7"],
  vehicle: ["M5 16h14l-1.5-6h-11Z", "M3 16v3h2", "M21 16v3h-2", "M7 19a2 2 0 1 0 4 0", "M13 19a2 2 0 1 0 4 0", "M7 10l2-4h6l2 4"],
};

const ICON_RULES: ReadonlyArray<readonly [ProductGroupIconName, RegExp]> = [
  ["shoe", /обув/],
  ["toy", /игруш/],
  ["mobility", /кресл.*коляск|реабилитац/],
  ["vehicle", /автозап|моторн.*масл|шин|покрыш/],
  ["tobacco", /табак|сигарет|никотин/],
  ["health", /(^|\s)бад($|\s)|ветеринар|лекарств|медицинск/],
  ["care", /антисеп|дезинфиц|космет|гигиен|духи|туалетн.*вод/],
  ["drink", /пиво|напит|алкогол|упакованн.*вод/],
  ["cable", /кабель|оптоволокн/],
  ["electronics", /радиоэлектрон|фотоаппарат|ламп.*вспыш/],
  ["book", /печатн/],
  ["construction", /строитель|полимерн.*труб|отопитель|пиротех/],
  ["apparel", /легк.*промышлен|мехов.*издел|текстил/],
  ["home", /дома|интерьер/],
  ["pet", /корм.*животн|удобрени/],
  ["food", /бакале|консерв|макарон|круп|мед|мёд|молоч|морепродукт|икр|мясн|полуфабрикат|заморож|растительн.*масл|сладост|детск.*питани|спортивн.*питани/],
];

export function createProductGroupIcon(groupName: string): HTMLElement {
  const iconName = productGroupIconName(groupName);
  const wrapper = document.createElement("span");
  wrapper.className = "product-group-icon";
  wrapper.dataset.icon = iconName;
  wrapper.setAttribute("aria-hidden", "true");

  const svg = document.createElementNS(SVG_NAMESPACE, "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("focusable", "false");
  for (const pathData of ICON_PATHS[iconName]) {
    const path = document.createElementNS(SVG_NAMESPACE, "path");
    path.setAttribute("d", pathData);
    svg.append(path);
  }
  wrapper.append(svg);
  return wrapper;
}

function productGroupIconName(groupName: string): ProductGroupIconName {
  const normalized = groupName.trim().toLocaleLowerCase("ru-RU").replaceAll("ё", "е");
  return ICON_RULES.find(([, pattern]) => pattern.test(normalized))?.[0] ?? "package";
}
