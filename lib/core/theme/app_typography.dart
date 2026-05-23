import 'package:flutter/material.dart';

class AppTypography {
  static const fontFamily = 'PlusJakartaSans';

  // Headlines
  static const headlineLarge = TextStyle(
    fontSize: 24,
    fontWeight: FontWeight.w700,
    height: 1.3,
    letterSpacing: -0.5,
  );
  static const headlineMedium = TextStyle(
    fontSize: 20,
    fontWeight: FontWeight.w600,
    height: 1.35,
  );
  
  // Post title - in feed card
  static const postTitle = TextStyle(
    fontSize: 16,
    fontWeight: FontWeight.w600,
    height: 1.4,
    letterSpacing: 0,
  );

  // Post content - in detail screen
  static const postContent = TextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w400,
    height: 1.65,
  );

  // Comment text
  static const commentText = TextStyle(
    fontSize: 14,
    fontWeight: FontWeight.w400,
    height: 1.5,
  );

  // Meta text (time, category)
  static const metaText = TextStyle(
    fontSize: 12,
    fontWeight: FontWeight.w400,
    letterSpacing: 0.2,
  );

  // Vote count - bold, readable
  static const voteCount = TextStyle(
    fontSize: 18,
    fontWeight: FontWeight.w700,
  );

  // Vote percentage - inside vote bar
  static const votePercent = TextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w700,
  );

  // Button label
  static const buttonLabel = TextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w600,
    letterSpacing: 0.3,
  );
}
