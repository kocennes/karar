import 'package:flutter/material.dart';

class AppColors {
  // Vote colors — light mode (WCAG AA with white text).
  static const hakli = Color(0xFF15803D);
  static const haksiz = Color(0xFFDC2626);
  static const hakliDim = Color(0xFF14532D);
  static const haksizDim = Color(0xFF7F1D1D);

  // Vote colors — dark mode (lightened for 6:1+ contrast on dark surfaces).
  static const darkHakli = Color(0xFF4ADE80);
  static const darkHaksiz = Color(0xFFEF4444);

  // Brand and CTA colors.
  static const primary = Color(0xFF4F46E5);
  static const accent = haksiz;

  // Light mode.
  static const background = Color(0xFFF8F9FA);
  static const surface = Color(0xFFFFFFFF);
  static const surfaceVariant = Color(0xFFF0F2F5);
  static const border = Color(0xFFE1E4E8);
  static const textPrimary = Color(0xFF0D0D0D);
  static const textSecondary = Color(0xFF4B5563);
  static const textTertiary = Color(0xFF6B7280);

  // Dark mode — Professional Tech palette (Material Design + Tailwind hybrid).
  // Background: #121212 avoids harsh halation on OLED vs pure black.
  // Surface: #1F2937 (Tailwind gray-800) — clear elevation above background.
  // SurfaceVariant: #2D3748 — nested/secondary elevation.
  // Border: #374151 (Tailwind gray-700) — visible card separation.
  // Text: off-white to avoid eye strain, not pure white.
  static const darkBackground = Color(0xFF111827);    // Tailwind gray-900
  static const darkSurface = Color(0xFF1F2937);       // Tailwind gray-800
  static const darkSurfaceVariant = Color(0xFF2D3748); // Tailwind gray-750
  static const darkBorder = Color(0xFF374151);         // Tailwind gray-700
  static const darkTextPrimary = Color(0xFFE8EAED);   // Off-white, no halation
  static const darkTextSecondary = Color(0xFF9CA3AF); // Tailwind gray-400
  static const darkTextTertiary = Color(0xFF6B7280);  // Tailwind gray-500

  // Status.
  static const success = hakli;
  static const warning = Color(0xFF92400E);
  static const error = haksiz;
}
