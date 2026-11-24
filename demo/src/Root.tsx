import React from "react";
import { Composition, Series, Still } from "remotion";
import {
  DONUT_FRAMES,
  DonutScene,
  END_FRAMES,
  EndCard,
  FIX_FRAMES,
  FixScene,
  LAYOUT_FRAMES,
  LayoutScene,
} from "./scenes";
import { SocialPreview } from "./SocialPreview";

const size = { fps: 30, width: 1280, height: 720 };

const Tour: React.FC = () => (
  <Series>
    <Series.Sequence durationInFrames={FIX_FRAMES}>
      <FixScene />
    </Series.Sequence>
    <Series.Sequence durationInFrames={DONUT_FRAMES}>
      <DonutScene />
    </Series.Sequence>
    <Series.Sequence durationInFrames={LAYOUT_FRAMES}>
      <LayoutScene />
    </Series.Sequence>
    <Series.Sequence durationInFrames={END_FRAMES}>
      <EndCard />
    </Series.Sequence>
  </Series>
);

export const RemotionRoot: React.FC = () => (
  <>
    <Composition id="Tour" component={Tour} durationInFrames={FIX_FRAMES + DONUT_FRAMES + LAYOUT_FRAMES + END_FRAMES} {...size} />
    <Composition id="Fix" component={FixScene} durationInFrames={FIX_FRAMES} {...size} />
    <Composition id="Donut" component={DonutScene} durationInFrames={DONUT_FRAMES} {...size} />
    <Composition id="Layout" component={LayoutScene} durationInFrames={LAYOUT_FRAMES} {...size} />
    <Still id="SocialPreview" component={SocialPreview} width={1280} height={640} />
  </>
);
