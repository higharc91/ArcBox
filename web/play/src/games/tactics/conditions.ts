/** A condition's id as words. A `statMod.<ability>` condition goes by its ability's last name part ("shield"). */
export const conditionName = (id: string): string =>
  (id.startsWith("statMod.") ? id.split(".").pop()! : id).replaceAll("_", " ");
