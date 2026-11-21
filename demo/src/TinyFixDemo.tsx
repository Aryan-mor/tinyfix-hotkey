import React from "react";
import { Easing, interpolate, useCurrentFrame, useVideoConfig } from "remotion";

const BG = "#10211C";
const CARD = "#F7F4EE";
const INK = "#1B2A26";
const MUTED = "#5C6B66";
const GREEN = "#125C44";
const LIME = "#C8E8C4";
const GOLD = "rgba(255, 214, 102, 0.55)";

type Token = {
  text: string;
  fixed?: string;
};

const TOKENS: Token[] = [
  { text: "I " },
  { text: "got " },
  { text: "a " },
  { text: "letter " },
  { text: "form", fixed: "from" },
  { text: " the " },
  { text: "bank. " },
  { text: "Their", fixed: "There" },
  { text: " is " },
  { text: "a " },
  { text: "problem, " },
  { text: "and " },
  { text: "I " },
  { text: "am " },
  { text: "to", fixed: "too" },
  { text: " tired " },
  { text: "to " },
  { text: "recieve", fixed: "receive" },
  { text: " it." },
];

const clamp = {
  extrapolateLeft: "clamp" as const,
  extrapolateRight: "clamp" as const,
  easing: Easing.bezier(0.16, 1, 0.3, 1),
};

const KeyCap: React.FC<{ label: string; pressed: number }> = ({ label, pressed }) => {
  return (
    <div
      style={{
        minWidth: label.length > 3 ? 160 : 92,
        height: 92,
        padding: "0 22px",
        borderRadius: 16,
        background: pressed > 0.5 ? GREEN : "#1A2E28",
        color: pressed > 0.5 ? "white" : "#E7F2EE",
        border: "2px solid rgba(255,255,255,0.12)",
        boxShadow: pressed > 0.5 ? "0 2px 0 #0A3326" : "0 8px 0 #0A3326",
        translate: `0px ${pressed * 6}px`,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 28,
        fontWeight: 700,
        letterSpacing: 0.4,
      }}
    >
      {label}
    </div>
  );
};

const WizardStep: React.FC<{ label: string; active: number; done: number }> = ({
  label,
  active,
  done,
}) => {
  const bg = done > 0.5 ? GREEN : active > 0.5 ? "#1A2E28" : "#15241F";
  return (
    <div
      style={{
        padding: "18px 36px",
        borderRadius: 999,
        background: bg,
        color: "white",
        fontSize: 36,
        fontWeight: 700,
        opacity: interpolate(Math.max(active, done), [0, 1], [0.35, 1], {
          extrapolateLeft: "clamp",
          extrapolateRight: "clamp",
        }),
        scale: interpolate(active, [0, 1], [0.94, 1], {
          extrapolateLeft: "clamp",
          extrapolateRight: "clamp",
          easing: Easing.spring({ damping: 200 }),
          output: "perceptual-scale",
        }),
        border: done > 0.5 ? "2px solid #8FDBB3" : "2px solid rgba(255,255,255,0.12)",
      }}
    >
      {label}
    </div>
  );
};

export const TinyFixDemo: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  const cardIn = interpolate(frame, [0, 0.7 * fps], [0, 1], clamp);
  const select = interpolate(frame, [1.8 * fps, 3.6 * fps], [0, 1], clamp);
  const keysIn = interpolate(frame, [3.8 * fps, 4.4 * fps], [0, 1], clamp);
  const press = interpolate(frame, [4.6 * fps, 5.1 * fps, 5.7 * fps, 6.2 * fps], [0, 1, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const morph = interpolate(frame, [6.3 * fps, 7.4 * fps], [0, 1], clamp);
  const glow = interpolate(frame, [6.3 * fps, 7.2 * fps, 9.2 * fps], [0, 1, 0], {
    extrapolateLeft: "clamp",
    extrapolateRight: "clamp",
  });
  const editorOut = interpolate(frame, [9.4 * fps, 10 * fps], [1, 0], clamp);
  const installIn = interpolate(frame, [9.6 * fps, 10.2 * fps], [0, 1], clamp);
  const step1 = interpolate(frame, [10.1 * fps, 10.4 * fps], [0, 1], clamp);
  const step2 = interpolate(frame, [10.8 * fps, 11.1 * fps], [0, 1], clamp);
  const step3 = interpolate(frame, [11.5 * fps, 11.8 * fps], [0, 1], clamp);
  const installOut = interpolate(frame, [12.2 * fps, 12.7 * fps], [1, 0], clamp);
  const endIn = interpolate(frame, [12.4 * fps, 13.2 * fps], [0, 1], clamp);

  return (
    <div
      style={{
        width: "100%",
        height: "100%",
        background: BG,
        fontFamily: 'Segoe UI, "Helvetica Neue", sans-serif',
        color: "white",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        overflow: "hidden",
      }}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          background:
            "radial-gradient(circle at 50% 20%, rgba(18,92,68,0.45), transparent 55%)",
        }}
      />

      <div
        style={{
          position: "absolute",
          top: 64,
          left: 80,
          display: "flex",
          alignItems: "center",
          gap: 16,
          opacity: interpolate(frame, [0, 0.5 * fps, 12.2 * fps, 12.6 * fps], [0, 1, 1, 0], clamp),
        }}
      >
        <div
          style={{
            width: 54,
            height: 54,
            borderRadius: 14,
            background: GREEN,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 32,
            fontWeight: 800,
          }}
        >
          ✓
        </div>
        <div style={{ fontSize: 34, fontWeight: 700, letterSpacing: -0.4 }}>TinyFix Hotkey</div>
      </div>

      <div
        style={{
          width: 1480,
          opacity: cardIn * editorOut,
          scale: interpolate(cardIn, [0, 1], [0.96, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
            output: "perceptual-scale",
          }),
          translate: `0px ${interpolate(cardIn, [0, 1], [24, 0], clamp)}px`,
        }}
      >
        <div
          style={{
            background: CARD,
            color: INK,
            borderRadius: 28,
            padding: "48px 56px 56px",
            boxShadow: "0 30px 80px rgba(0,0,0,0.35)",
          }}
        >
          <div style={{ display: "flex", gap: 10, marginBottom: 28 }}>
            <div style={{ width: 14, height: 14, borderRadius: 99, background: "#E7A6A0" }} />
            <div style={{ width: 14, height: 14, borderRadius: 99, background: "#E6D08A" }} />
            <div style={{ width: 14, height: 14, borderRadius: 99, background: "#A8D5B5" }} />
            <div style={{ marginLeft: 18, color: MUTED, fontSize: 22 }}>Untitled.txt</div>
          </div>
          <div
            style={{
              fontSize: 46,
              lineHeight: 1.45,
              fontWeight: 500,
              position: "relative",
            }}
          >
            <div
              style={{
                position: "absolute",
                inset: "-8px -12px",
                background: GOLD,
                width: `${select * 100}%`,
                borderRadius: 8,
                opacity: select * (1 - morph * 0.85),
              }}
            />
            <span style={{ position: "relative" }}>
              {TOKENS.map((token, index) => {
                const showFixed = Boolean(token.fixed) && morph > 0.5;
                const isFix = Boolean(token.fixed);
                return (
                  <span
                    key={`${token.text}-${index}`}
                    style={{
                      background: isFix && glow > 0.05 ? `rgba(18, 92, 68, ${0.18 + glow * 0.28})` : "transparent",
                      borderRadius: 8,
                      boxShadow: isFix && glow > 0.05 ? `0 0 0 6px rgba(18, 92, 68, ${0.1 + glow * 0.16})` : "none",
                      color: isFix && showFixed ? GREEN : INK,
                      fontWeight: isFix && showFixed ? 750 : 500,
                    }}
                  >
                    {showFixed ? token.fixed : token.text}
                  </span>
                );
              })}
            </span>
          </div>
        </div>

        <div
          style={{
            marginTop: 42,
            display: "flex",
            justifyContent: "center",
            alignItems: "center",
            gap: 16,
            opacity: keysIn,
            scale: interpolate(keysIn, [0, 1], [0.9, 1], {
              extrapolateLeft: "clamp",
              extrapolateRight: "clamp",
              output: "perceptual-scale",
            }),
          }}
        >
          <KeyCap label="Ctrl" pressed={press} />
          <span style={{ fontSize: 36, opacity: 0.6 }}>+</span>
          <KeyCap label="Alt" pressed={press} />
          <span style={{ fontSize: 36, opacity: 0.6 }}>+</span>
          <KeyCap label="Space" pressed={press} />
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 36,
          opacity: installIn * installOut,
        }}
      >
        <div style={{ fontSize: 28, letterSpacing: 4, textTransform: "uppercase", color: LIME }}>
          Install
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 22 }}>
          <WizardStep label="Next" active={step1} done={step2} />
          <div style={{ fontSize: 40, opacity: 0.55 }}>→</div>
          <WizardStep label="Next" active={step2} done={step3} />
          <div style={{ fontSize: 40, opacity: 0.55 }}>→</div>
          <WizardStep label="Finish" active={step3} done={step3} />
        </div>
      </div>

      <div
        style={{
          position: "absolute",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 22,
          opacity: endIn,
          scale: interpolate(endIn, [0, 1], [0.96, 1], {
            extrapolateLeft: "clamp",
            extrapolateRight: "clamp",
            output: "perceptual-scale",
          }),
        }}
      >
        <div
          style={{
            width: 96,
            height: 96,
            borderRadius: 24,
            background: GREEN,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            fontSize: 56,
            fontWeight: 800,
            boxShadow: "0 16px 40px rgba(18,92,68,0.45)",
          }}
        >
          ✓
        </div>
        <div style={{ fontSize: 72, fontWeight: 800, letterSpacing: -1.4 }}>TinyFix Hotkey</div>
        <div style={{ fontSize: 36, color: LIME, fontWeight: 600 }}>free, offline</div>
        <div style={{ fontSize: 30, color: "rgba(255,255,255,0.72)", marginTop: 8 }}>
          github.com/Aryan-mor/tinyfix-hotkey
        </div>
      </div>
    </div>
  );
};
