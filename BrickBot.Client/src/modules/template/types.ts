/** Mirrors BrickBot/Modules/Template/Models/TemplateInfo.cs. */
export interface TemplateInfo {
  /** Stable identifier (Guid). On-disk filename is `{id}.png`. */
  id: string;
  name: string;
  description?: string;
  width: number;
  height: number;
  /** ISO-8601 timestamp string (camelCase; serialized from DateTimeOffset). */
  createdAt: string;
  updatedAt: string;
}

export interface CropRect {
  x: number;
  y: number;
  w: number;
  h: number;
}
