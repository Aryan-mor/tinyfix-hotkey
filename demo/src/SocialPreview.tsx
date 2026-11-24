// GitHub social preview (1280x640): Settings > General > Social preview.
import React from "react";
import { Donut, Chip } from "./scenes";
import { Logo, Stage, T } from "./ui";

export const SocialPreview: React.FC = () => (
  <Stage>
    <div style={{ position: "absolute", left: 80, top: 0, bottom: 0, width: 640, display: "flex", flexDirection: "column", justifyContent: "center", gap: 22 }}>
      <div style={{ display: "flex", alignItems: "center", gap: 18 }}>
        <Logo size={72} />
        <div style={{ fontSize: 72, fontWeight: 800, letterSpacing: -2 }}>TinyFix</div>
      </div>
      <div style={{ fontSize: 38, fontWeight: 650, lineHeight: 1.2, letterSpacing: -0.5 }}>
        Select text in any app.
        <br />
        Press one shortcut. It&apos;s fixed.
      </div>
      <div style={{ fontSize: 22, color: "rgba(255,255,255,0.7)", lineHeight: 1.4 }}>
        Fix, translate and rewrite with any AI model you choose. A donut menu for all your skills.
      </div>
      <div style={{ display: "flex", gap: 10, marginTop: 6 }}>
        <Chip>Free · MIT</Chip>
        <Chip>Windows 10/11</Chip>
      </div>
    </div>
    <div style={{ position: "absolute", right: 70, top: 120, display: "flex", flexDirection: "column", alignItems: "center", gap: 26 }}>
      <Donut open={1} active={0} size={380} />
      <div
        style={{
          background: T.surface,
          color: T.text,
          borderRadius: 14,
          padding: "12px 20px",
          fontSize: 22,
          boxShadow: "0 16px 40px rgba(0,0,0,0.4)",
        }}
      >
        <span style={{ textDecoration: "line-through", color: T.muted }}>recieve</span>{" "}
        <span style={{ color: T.success, fontWeight: 700 }}>→ receive</span>
      </div>
    </div>
  </Stage>
);
