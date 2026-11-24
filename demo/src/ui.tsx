// Shared look for the demo scenes: the app's own "Soft" theme colours (src/TinyFix.App/Styles/Soft.axaml).
import React from "react";
import { Easing, interpolate, useCurrentFrame } from "remotion";

export const T = {
  page: "#1B1B19",
  pageLight: "#F6F5F1",
  surface: "#FFFFFF",
  surfaceAlt: "#EFEDE6",
  border: "rgba(0,0,0,0.09)",
  text: "#1F1E1D",
  muted: "#6B6A66",
  accent: "#7F77DD",
  accentStrong: "#5F59A6",
  accentLight: "#928BE2",
  select: "rgba(127,119,221,0.32)",
  fixed: "rgba(15,122,90,0.16)",
  success: "#0F7A5A",
  successDark: "#5DCAA5",
  font: '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif',
};

const bezier = Easing.bezier(0.16, 1, 0.3, 1);

/** 0 to 1 between two frames, eased and clamped. */
export const ease = (frame: number, from: number, to: number) =>
  interpolate(frame, [from, to], [0, 1], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
    easing: bezier,
  });

/** 0 -> 1 -> 0: up over [a,b], down over [c,d]. */
export const pulse = (frame: number, a: number, b: number, c: number, d: number) =>
  interpolate(frame, [a, b, c, d], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });

/** Fades a whole scene in and out at its edges. */
export const useSceneFade = (duration: number, edge = 10) => {
  const frame = useCurrentFrame();
  return interpolate(frame, [0, edge, duration - edge, duration], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
};

export const Stage: React.FC<{ children: React.ReactNode; opacity?: number }> = ({ children, opacity = 1 }) => (
  <div
    style={{
      width: "100%",
      height: "100%",
      background: T.page,
      fontFamily: T.font,
      color: "white",
      position: "relative",
      overflow: "hidden",
    }}
  >
    <div
      style={{
        position: "absolute",
        inset: 0,
        background:
          "radial-gradient(ellipse at 50% 0%, rgba(127,119,221,0.30), transparent 60%), radial-gradient(ellipse at 100% 100%, rgba(93,202,165,0.10), transparent 50%)",
      }}
    />
    <div style={{ position: "absolute", inset: 0, opacity }}>{children}</div>
  </div>
);

/** The scene's title, top left: what the viewer is about to see. */
export const Caption: React.FC<{ title: string; sub: string }> = ({ title, sub }) => {
  const frame = useCurrentFrame();
  const t = ease(frame, 2, 18);
  return (
    <div
      style={{
        position: "absolute",
        top: 40,
        left: 56,
        opacity: t,
        translate: `${(1 - t) * -16}px 0px`,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
        <Logo size={34} />
        <div style={{ fontSize: 28, fontWeight: 700, letterSpacing: -0.3 }}>{title}</div>
      </div>
      <div style={{ fontSize: 19, color: "rgba(255,255,255,0.62)", marginTop: 6, marginLeft: 46 }}>{sub}</div>
    </div>
  );
};

export const Logo: React.FC<{ size: number }> = ({ size }) => (
  <div
    style={{
      width: size,
      height: size,
      borderRadius: size * 0.28,
      background: `linear-gradient(140deg, ${T.accentLight}, ${T.accentStrong})`,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      boxShadow: "0 4px 14px rgba(127,119,221,0.45)",
    }}
  >
    <svg width={size * 0.56} height={size * 0.56} viewBox="0 0 24 24">
      <path d="M4 12.5l5 5L20 6.5" fill="none" stroke="white" strokeWidth={3.2} strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  </div>
);

/** A light app window standing in for "any app". */
export const AppWindow: React.FC<{
  app: string;
  title: string;
  width: number;
  children: React.ReactNode;
  style?: React.CSSProperties;
}> = ({ app, title, width, children, style }) => (
  <div
    style={{
      width,
      background: T.surface,
      color: T.text,
      borderRadius: 18,
      boxShadow: "0 1px 3px rgba(0,0,0,0.2), 0 24px 70px rgba(0,0,0,0.45)",
      overflow: "hidden",
      ...style,
    }}
  >
    <div
      style={{
        height: 46,
        background: T.surfaceAlt,
        display: "flex",
        alignItems: "center",
        padding: "0 18px",
        gap: 10,
        borderBottom: `1px solid ${T.border}`,
      }}
    >
      <div style={{ fontSize: 15, fontWeight: 700, color: T.text }}>{app}</div>
      <div style={{ fontSize: 15, color: T.muted }}>{title}</div>
      <div style={{ flex: 1 }} />
      {["—", "▢", "✕"].map((c) => (
        <div key={c} style={{ fontSize: 13, color: T.muted, width: 26, textAlign: "center" }}>
          {c}
        </div>
      ))}
    </div>
    <div style={{ padding: "26px 30px 30px" }}>{children}</div>
  </div>
);

export const Caret: React.FC<{ height?: number; visible?: boolean }> = ({ height = 34, visible = true }) => {
  const frame = useCurrentFrame();
  const on = Math.floor(frame / 15) % 2 === 0;
  return (
    <span
      style={{
        display: "inline-block",
        width: 2.5,
        height,
        background: T.accentStrong,
        verticalAlign: "text-bottom",
        marginLeft: 2,
        opacity: visible && on ? 1 : 0,
      }}
    />
  );
};

const KeyCap: React.FC<{ label: string; pressed: number }> = ({ label, pressed }) => (
  <div
    style={{
      minWidth: label.length > 1 ? 96 : 62,
      height: 62,
      padding: "0 18px",
      borderRadius: 12,
      background: pressed > 0.5 ? T.accent : "#2A2A27",
      color: "white",
      border: "1.5px solid rgba(255,255,255,0.14)",
      boxShadow: pressed > 0.5 ? `0 1px 0 ${T.accentStrong}` : "0 5px 0 #0E0E0D",
      translate: `0px ${pressed * 4}px`,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      fontSize: 22,
      fontWeight: 650,
    }}
  >
    {label}
  </div>
);

/** A shortcut shown at the bottom: fades in at `inAt`, pressed between `pressAt` and `pressAt + 14`. */
export const Keys: React.FC<{ keys: string[]; inAt: number; pressAt: number; outAt?: number }> = ({
  keys,
  inAt,
  pressAt,
  outAt = 9999,
}) => {
  const frame = useCurrentFrame();
  const show = ease(frame, inAt, inAt + 10) * (1 - ease(frame, outAt, outAt + 10));
  const press = pulse(frame, pressAt, pressAt + 3, pressAt + 12, pressAt + 15);
  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        right: 0,
        bottom: 44,
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        gap: 12,
        opacity: show,
        scale: String(0.92 + show * 0.08),
      }}
    >
      {keys.map((k, i) => (
        <React.Fragment key={k + i}>
          {i > 0 && <span style={{ fontSize: 24, opacity: 0.5 }}>+</span>}
          <KeyCap label={k} pressed={press} />
        </React.Fragment>
      ))}
    </div>
  );
};

/** The "text changed" toast with Undo, as TinyFix shows after a replacement. */
export const Toast: React.FC<{ text: string; inAt: number; outAt?: number }> = ({ text, inAt, outAt = 9999 }) => {
  const frame = useCurrentFrame();
  const t = ease(frame, inAt, inAt + 12) * (1 - ease(frame, outAt, outAt + 10));
  return (
    <div
      style={{
        position: "absolute",
        right: 56,
        bottom: 56,
        opacity: t,
        translate: `0px ${(1 - t) * 24}px`,
        background: "#262624",
        border: "1px solid rgba(255,255,255,0.12)",
        borderRadius: 14,
        padding: "14px 18px",
        display: "flex",
        alignItems: "center",
        gap: 14,
        boxShadow: "0 12px 30px rgba(0,0,0,0.45)",
        fontSize: 19,
      }}
    >
      <div
        style={{
          width: 26,
          height: 26,
          borderRadius: 99,
          background: T.successDark,
          color: "#0E2A20",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontWeight: 800,
          fontSize: 16,
        }}
      >
        ✓
      </div>
      <div style={{ color: "#F1EFE8" }}>{text}</div>
      <div
        style={{
          marginLeft: 8,
          color: T.accentLight,
          fontWeight: 650,
          padding: "4px 12px",
          borderRadius: 8,
          background: "rgba(127,119,221,0.16)",
        }}
      >
        Undo
      </div>
    </div>
  );
};

export type Token = { text: string; fixed?: string };

/**
 * Text made of tokens. `select` (0..1) highlights tokens from the left; after `morph` passes 0.5 each token with a
 * `fixed` value shows it, tinted green while `glow` is up.
 */
export const MorphText: React.FC<{
  tokens: Token[];
  select: number;
  morph: number;
  glow: number;
  caret?: boolean;
  fontSize?: number;
}> = ({ tokens, select, morph, glow, caret = false, fontSize = 34 }) => {
  const n = tokens.length;
  return (
    <div style={{ fontSize, lineHeight: 1.5, fontWeight: 450 }}>
      {tokens.map((tok, i) => {
        const selected = select > 0 && i / n < select && morph < 0.5;
        const changed = Boolean(tok.fixed) && morph >= 0.5;
        return (
          <span
            key={i}
            style={{
              background: selected ? T.select : changed && glow > 0.02 ? `rgba(15,122,90,${0.18 * glow})` : "transparent",
              borderRadius: 5,
              color: changed && glow > 0.3 ? T.success : T.text,
              fontWeight: changed && glow > 0.3 ? 600 : 450,
              whiteSpace: "pre-wrap",
            }}
          >
            {changed ? tok.fixed : tok.text}
          </span>
        );
      })}
      {caret && <Caret height={fontSize * 1.1} visible={select === 0 || morph >= 1} />}
    </div>
  );
};
