// Turns the rendered videos in out/ into README GIFs (960 px, 15 fps, one palette per clip) and copies them,
// the tour video and the social preview into ../docs. Needs ffmpeg on PATH. Run `pnpm render` and
// `pnpm render:still` first.
import { execFileSync } from "node:child_process";
import { copyFileSync } from "node:fs";

const filter =
  "fps=15,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];" +
  "[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle";

const clips = { Fix: "fix.gif", Donut: "donut.gif", Layout: "layout.gif", Tour: "demo.gif" };

for (const [name, target] of Object.entries(clips)) {
  execFileSync("ffmpeg", ["-v", "error", "-y", "-i", `out/${name}.mp4`, "-vf", filter, `out/${name}.gif`], { stdio: "inherit" });
  copyFileSync(`out/${name}.gif`, `../docs/${target}`);
  console.log(`docs/${target}`);
}
copyFileSync("out/Tour.mp4", "../docs/demo.mp4");
copyFileSync("out/social-preview.png", "../docs/social-preview.png");
console.log("docs/demo.mp4, docs/social-preview.png");
