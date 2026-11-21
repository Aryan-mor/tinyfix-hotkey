import React from "react";
import { Composition } from "remotion";
import { TinyFixDemo } from "./TinyFixDemo";

export const RemotionRoot: React.FC = () => {
  return (
    <Composition
      id="TinyFixDemo"
      component={TinyFixDemo}
      durationInFrames={450}
      fps={30}
      width={1920}
      height={1080}
    />
  );
};
