/**
 * Built-in languages shipped with Ohno. C#, TypeScript, and
 * JavaScript are on by default. Untyped JS stays honest (`C(name)`
 * / Unknown) rather than inventing a bound.
 *
 * Do not treat this list as the long-term analyzer registry. A public
 * analyzer manifest schema (and optional file upload) will later let
 * third-party analyzers declare extra languageIds. Uploaded analyzers
 * should stay opt-in and must not override a built-in unless the user
 * enables that explicitly.
 */
export interface BuiltinLanguage {
  id: string;
  title: string;
  enabledByDefault: boolean;
}

export const DEFAULT_LANGUAGE_ID = 'csharp';

export const BUILTIN_LANGUAGES: readonly BuiltinLanguage[] = [
  { id: 'csharp', title: 'C#', enabledByDefault: true },
  { id: 'typescript', title: 'TypeScript', enabledByDefault: true },
  { id: 'javascript', title: 'JavaScript', enabledByDefault: true },
  {
    id: 'typescriptreact',
    title: 'TypeScript React',
    enabledByDefault: true,
  },
  {
    id: 'javascriptreact',
    title: 'JavaScript React',
    enabledByDefault: true,
  },
];

export function builtinLanguageIds(): string[] {
  return BUILTIN_LANGUAGES.map((language) => language.id);
}

export function documentSelectors(): { language: string }[] {
  return builtinLanguageIds().map((language) => ({ language }));
}

export function defaultLanguageEnabled(languageId: string): boolean {
  const language = BUILTIN_LANGUAGES.find(
    (item) => item.id === languageId,
  );
  return language?.enabledByDefault ?? false;
}

export function isCsharpLanguage(languageId: string): boolean {
  return languageId === 'csharp';
}
