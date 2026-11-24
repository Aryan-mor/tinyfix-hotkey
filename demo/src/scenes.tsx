import React from "react";
import { useCurrentFrame } from "remotion";
import { AppWindow, Caption, Caret, Keys, Logo, MorphText, Stage, T, Toast, Token, ease, pulse, useSceneFade } from "./ui";

// ---------------------------------------------------------------------------------------------------------------
// 1. Fix in place: select, Ctrl+Alt+Space, the corrected text replaces it, a toast offers Undo.

export const FIX_FRAMES = 210;

const FIX_TOKENS: Token[] = [
  { text: "hey", fixed: "Hey" },
  { text: " team," },
  { text: " i", fixed: " I" },
  { text: " cant", fixed: " can't" },
  { text: " make" },
  { text: " it" },
  { text: " to" },
  { text: " the" },
  { text: " meating", fixed: " meeting" },
  { text: " tomorow,", fixed: " tomorrow." },
  { text: " can", fixed: " Can" },
  { text: " we" },
  { text: " move" },
  { text: " it" },
  { text: " to" },
  { text: " friday?", fixed: " Friday?" },
  { text: " sory", fixed: " Sorry" },
  { text: " for" },
  { text: " the" },
  { text: " late" },
  { text: " notise", fixed: " notice." },
];

const ChatHistory: React.FC = () => (
  <div style={{ display: "flex", gap: 14, marginBottom: 22 }}>
    <div style={{ width: 40, height: 40, borderRadius: 10, background: "#E3B58A", flexShrink: 0 }} />
    <div>
      <div style={{ fontSize: 17, fontWeight: 700 }}>
        Maya <span style={{ fontWeight: 400, color: T.muted, fontSize: 14 }}>10:42</span>
      </div>
      <div style={{ fontSize: 20, color: T.text, marginTop: 2 }}>Design review is tomorrow at 10. Everyone good?</div>
    </div>
  </div>
);

export const FixScene: React.FC = () => {
  const frame = useCurrentFrame();
  const fade = useSceneFade(FIX_FRAMES);
  const select = ease(frame, 30, 64);
  const morph = frame >= 94 ? 1 : 0;
  const glow = pulse(frame, 94, 100, 160, 190);
  return (
    <Stage opacity={fade}>
      <Caption title="Fix in place" sub="Select text in any app and press one shortcut." />
      <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center" }}>
        <AppWindow app="Chat" title="# design-team" width={940} style={{ marginTop: -10 }}>
          <ChatHistory />
          <div style={{ border: `1.5px solid ${T.border}`, borderRadius: 14, padding: "16px 20px", background: T.pageLight }}>
            <MorphText tokens={FIX_TOKENS} select={select} morph={morph} glow={glow} caret fontSize={30} />
          </div>
        </AppWindow>
      </div>
      <Keys keys={["Ctrl", "Alt", "Space"]} inAt={62} pressAt={76} outAt={150} />
      <Toast text="Text changed" inAt={104} outAt={190} />
    </Stage>
  );
};

// ---------------------------------------------------------------------------------------------------------------
// 2. Donut menu: Ctrl+Alt+M opens a ring of skills next to the caret; press 2 for Translate.

export const DONUT_FRAMES = 240;

const SLICES = ["Fix", "Translate", "Rewrite", "Improve prompt", "Swap EN ⇄ FA"];

const polar = (cx: number, cy: number, r: number, a: number) => [cx + r * Math.cos(a), cy + r * Math.sin(a)];

const sector = (cx: number, cy: number, R: number, r: number, a0: number, a1: number) => {
  const gapO = 3 / R;
  const gapI = 3 / r;
  const [x0, y0] = polar(cx, cy, R, a0 + gapO);
  const [x1, y1] = polar(cx, cy, R, a1 - gapO);
  const [x2, y2] = polar(cx, cy, r, a1 - gapI);
  const [x3, y3] = polar(cx, cy, r, a0 + gapI);
  const large = a1 - a0 > Math.PI ? 1 : 0;
  return `M${x0},${y0} A${R},${R} 0 ${large} 1 ${x1},${y1} L${x2},${y2} A${r},${r} 0 ${large} 0 ${x3},${y3} Z`;
};

export const Donut: React.FC<{ open: number; active: number; size?: number }> = ({ open, active, size = 340 }) => {
  const R = size / 2 - 8;
  const r = R * 0.42;
  const c = size / 2;
  const n = SLICES.length;
  const step = (Math.PI * 2) / n;
  const start = -Math.PI / 2 - step / 2;
  return (
    <div
      style={{
        width: size,
        height: size,
        position: "relative",
        opacity: open,
        scale: String(0.6 + 0.4 * open),
        rotate: `${(1 - open) * -40}deg`,
        filter: "drop-shadow(0 18px 30px rgba(0,0,0,0.45))",
      }}
    >
      <svg width={size} height={size}>
        {SLICES.map((label, i) => {
          const a0 = start + i * step;
          const a1 = a0 + step;
          const on = i === active;
          const pop = on ? 7 : 0;
          const mid = (a0 + a1) / 2;
          const [dx, dy] = [Math.cos(mid) * pop, Math.sin(mid) * pop];
          const [lx, ly] = polar(c, c, (R + r) / 2, mid);
          return (
            <g key={label} transform={`translate(${dx},${dy})`}>
              <path
                d={sector(c, c, R, r, a0, a1)}
                fill={on ? T.accent : "rgba(255,255,255,0.96)"}
                stroke="rgba(0,0,0,0.08)"
              />
              <text
                x={lx}
                y={ly - 10}
                textAnchor="middle"
                fontSize={12}
                fontWeight={700}
                fill={on ? "rgba(255,255,255,0.85)" : T.muted}
                fontFamily={T.font}
              >
                {i + 1}
              </text>
              <text
                x={lx}
                y={ly + 9}
                textAnchor="middle"
                fontSize={label.length > 10 ? 13 : 15}
                fontWeight={650}
                fill={on ? "white" : T.text}
                fontFamily={T.font}
              >
                {label}
              </text>
            </g>
          );
        })}
        <circle cx={c} cy={c} r={r - 6} fill="white" stroke="rgba(0,0,0,0.06)" />
        <text x={c} y={c - 2} textAnchor="middle" fontSize={15} fontWeight={700} fill={T.accentStrong} fontFamily={T.font}>
          {active >= 0 ? SLICES[active] : "TinyFix"}
        </text>
        <text x={c} y={c + 17} textAnchor="middle" fontSize={11} fill={T.muted} fontFamily={T.font}>
          1–9 · Tab
        </text>
      </svg>
    </div>
  );
};

const MAIL_EN = "Thanks for the quick reply! I'll send the files tonight. 🙌";
const MAIL_ES = "¡Gracias por la respuesta tan rápida! Te mando los archivos esta noche. 🙌";

export const DonutScene: React.FC = () => {
  const frame = useCurrentFrame();
  const fade = useSceneFade(DONUT_FRAMES);
  const select = ease(frame, 16, 40);
  const open = ease(frame, 62, 80) * (1 - ease(frame, 122, 132));
  const active = frame >= 108 ? 1 : frame >= 80 ? 0 : -1;
  const translated = frame >= 136;
  const glow = pulse(frame, 136, 142, 190, 215);
  return (
    <Stage opacity={fade}>
      <Caption title="The donut menu" sub="Ctrl+Alt+M opens your skills next to the caret. Pick one, or press 1–9." />
      <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center" }}>
        <AppWindow app="Mail" title="Re: project files" width={900} style={{ marginTop: -10 }}>
          <div style={{ fontSize: 16, color: T.muted, borderBottom: `1px solid ${T.border}`, paddingBottom: 12, marginBottom: 18 }}>
            To: <span style={{ color: T.text }}>Lucía Ramos</span>
          </div>
          <div style={{ fontSize: 30, lineHeight: 1.5, minHeight: 140 }}>
            <span
              style={{
                background: translated
                  ? `rgba(15,122,90,${0.18 * glow})`
                  : `linear-gradient(90deg, ${T.select} ${select * 100}%, transparent ${select * 100}%)`,
                color: translated && glow > 0.3 ? T.success : T.text,
                borderRadius: 5,
                fontWeight: translated && glow > 0.3 ? 600 : 450,
              }}
            >
              {translated ? MAIL_ES : MAIL_EN}
            </span>
            {translated && <Caret height={33} />}
          </div>
          <div style={{ fontSize: 18, color: T.muted, marginTop: 8 }}>— Sam</div>
        </AppWindow>
      </div>
      <div style={{ position: "absolute", left: 850, top: 352 }}>
        <Donut open={open} active={active} />
      </div>
      <Keys keys={["Ctrl", "Alt", "M"]} inAt={38} pressAt={52} outAt={82} />
      <Keys keys={["2"]} inAt={94} pressAt={104} outAt={128} />
      <Toast text="Translated" inAt={146} outAt={222} />
    </Stage>
  );
};

// ---------------------------------------------------------------------------------------------------------------
// 3. Wrong keyboard layout: "sghl" typed on the English layout becomes "سلام". No model, no internet.

export const LAYOUT_FRAMES = 190;

export const LayoutScene: React.FC = () => {
  const frame = useCurrentFrame();
  const fade = useSceneFade(LAYOUT_FRAMES);
  const typed = "sghl".slice(0, Math.max(0, Math.min(4, Math.floor((frame - 14) / 6) + 1)));
  const select = ease(frame, 52, 62);
  const converted = frame >= 92;
  const glow = pulse(frame, 92, 98, 150, 175);
  const badge = ease(frame, 104, 118);
  return (
    <Stage opacity={fade}>
      <Caption title="Wrong keyboard layout?" sub="Typed Persian words on the English layout? Ctrl+Alt+L swaps them back." />
      <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center" }}>
        <AppWindow app="Messages" title="Sara" width={760} style={{ marginTop: -10 }}>
          <div style={{ display: "flex", justifyContent: "flex-start", marginBottom: 20 }}>
            <div style={{ background: T.surfaceAlt, borderRadius: 16, padding: "10px 16px", fontSize: 22, direction: "rtl" }}>
              سلام، خوبی؟ 👋
            </div>
          </div>
          <div
            style={{
              border: `1.5px solid ${T.border}`,
              borderRadius: 14,
              padding: "14px 20px",
              background: T.pageLight,
              fontSize: 40,
              minHeight: 64,
              direction: converted ? "rtl" : "ltr",
              textAlign: converted ? "right" : "left",
            }}
          >
            <span
              style={{
                background: converted
                  ? `rgba(15,122,90,${0.18 * glow})`
                  : select > 0
                    ? `linear-gradient(90deg, ${T.select} ${select * 100}%, transparent ${select * 100}%)`
                    : "transparent",
                color: converted && glow > 0.3 ? T.success : T.text,
                fontWeight: converted && glow > 0.3 ? 600 : 450,
                borderRadius: 5,
                padding: "0 2px",
              }}
            >
              {converted ? "سلام" : typed}
            </span>
            <Caret height={44} visible={select === 0 || converted} />
          </div>
          <div
            style={{
              marginTop: 18,
              display: "flex",
              gap: 10,
              opacity: badge,
              translate: `0px ${(1 - badge) * 10}px`,
            }}
          >
            {["No AI model", "No internet", "Uses your installed layouts"].map((b) => (
              <div
                key={b}
                style={{
                  fontSize: 16,
                  fontWeight: 600,
                  color: T.accentStrong,
                  background: "rgba(127,119,221,0.12)",
                  padding: "6px 12px",
                  borderRadius: 99,
                }}
              >
                {b}
              </div>
            ))}
          </div>
        </AppWindow>
      </div>
      <Keys keys={["Ctrl", "Alt", "L"]} inAt={60} pressAt={76} outAt={140} />
      <Toast text="Converted" inAt={100} outAt={178} />
    </Stage>
  );
};

// ---------------------------------------------------------------------------------------------------------------
// End card.

export const END_FRAMES = 120;

export const Chip: React.FC<{ children: React.ReactNode }> = ({ children }) => (
  <div
    style={{
      fontSize: 20,
      fontWeight: 600,
      padding: "8px 18px",
      borderRadius: 99,
      background: "rgba(127,119,221,0.16)",
      color: "#D9D6F7",
      border: "1px solid rgba(127,119,221,0.35)",
    }}
  >
    {children}
  </div>
);

export const EndCard: React.FC = () => {
  const frame = useCurrentFrame();
  const fade = useSceneFade(END_FRAMES, 8);
  const t = ease(frame, 0, 22);
  return (
    <Stage opacity={fade}>
      <div
        style={{
          position: "absolute",
          inset: 0,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: 20,
          scale: String(0.96 + 0.04 * t),
        }}
      >
        <Logo size={92} />
        <div style={{ fontSize: 76, fontWeight: 800, letterSpacing: -2 }}>TinyFix</div>
        <div style={{ fontSize: 30, color: "rgba(255,255,255,0.82)" }}>
          Fix, translate and rewrite selected text in any app.
        </div>
        <div style={{ display: "flex", gap: 12, marginTop: 8 }}>
          <Chip>Any AI model you choose</Chip>
          <Chip>Free · MIT</Chip>
          <Chip>Windows 10/11</Chip>
        </div>
        <div style={{ fontSize: 22, color: "rgba(255,255,255,0.6)", marginTop: 14 }}>
          github.com/Aryan-mor/tinyfix-hotkey
        </div>
      </div>
    </Stage>
  );
};
