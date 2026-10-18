package org.kenjinx.android

import android.content.res.Resources
import androidx.compose.runtime.mutableIntStateOf
import androidx.preference.PreferenceManager
import org.json.JSONObject

/**
 * Traducao do aplicativo.
 *
 * Os textos no codigo continuam escritos em portugues (Brasil) e passam por [tr].
 * Para os outros idiomas existe um dicionario em assets/i18n/<tag>.json no formato
 * { "texto original": "traducao" }. Texto sem traducao aparece no original, nunca quebra.
 *
 * Placeholders: {0}, {1}... sao trocados pelos argumentos passados a [tr].
 *
 * Escolha do idioma:
 *  1. preferencia do usuario (Configuracoes > Sistema > Idioma do aplicativo), ou
 *  2. "Seguir o sistema": primeiro idioma da lista do Android que o app suporta, ou
 *  3. ingles, quando nenhum idioma do sistema for suportado.
 */
object I18n {
    const val SYSTEM = "system"
    private const val PREF_KEY = "app_language"
    private const val BASE_TAG = "pt-BR"
    // This fork defaults to Simplified Chinese out of the box.
    private const val DEFAULT_TAG = "zh-CN"
    private const val FALLBACK_TAG = "zh-CN"

    data class Language(val tag: String, val nativeName: String)

    /** Nomes no proprio idioma: nao sao traduzidos. */
    val languages: List<Language> = listOf(
        Language("pt-BR", "Português (Brasil)"),
        Language("en", "English"),
        Language("es", "Español"),
        Language("ru", "Русский"),
        Language("zh-CN", "简体中文"),
        Language("ja", "日本語"),
        Language("ko", "한국어")
    )

    // Lido dentro de tr(): composables que usam tr() recompoem sozinhos quando o idioma muda.
    private val version = mutableIntStateOf(0)

    @Volatile
    private var initialized = false

    @Volatile
    private var preference: String = SYSTEM

    @Volatile
    private var activeTag: String = BASE_TAG

    @Volatile
    private var dictionary: Map<String, String> = emptyMap()

    private fun ensureInit() {
        if (initialized) return
        synchronized(this) {
            if (initialized) return
            preference = try {
                PreferenceManager.getDefaultSharedPreferences(KenjinxApplication.context)
                    .getString(PREF_KEY, DEFAULT_TAG) ?: DEFAULT_TAG
            } catch (_: Throwable) {
                DEFAULT_TAG
            }
            if (preference != SYSTEM && languages.none { it.tag == preference }) preference = SYSTEM
            applyLocked()
            initialized = true
        }
    }

    /** Chamado quando uma tela abre; reaplica o idioma caso o sistema tenha mudado de idioma. */
    fun refresh() {
        ensureInit()
        if (preference != SYSTEM) return
        val tag = systemTag()
        if (tag != activeTag) {
            synchronized(this) { applyLocked() }
            version.intValue++
        }
    }

    /** Preferencia atual: [SYSTEM] ou uma tag de [languages]. Observavel pelo Compose. */
    fun currentPreference(): String {
        ensureInit()
        version.intValue
        return preference
    }

    /** Tag realmente em uso (ex.: "en"). Observavel pelo Compose. */
    fun currentTag(): String {
        ensureInit()
        version.intValue
        return activeTag
    }

    fun setPreference(value: String) {
        ensureInit()
        val newValue = if (languages.any { it.tag == value }) value else SYSTEM
        try {
            PreferenceManager.getDefaultSharedPreferences(KenjinxApplication.context)
                .edit().putString(PREF_KEY, newValue).apply()
        } catch (_: Throwable) {
        }
        synchronized(this) {
            preference = newValue
            applyLocked()
        }
        version.intValue++
    }

    fun translate(text: String, args: Array<out Any?>): String {
        ensureInit()
        version.intValue
        var out = dictionary[text] ?: text
        if (args.isNotEmpty()) {
            for (i in args.indices) {
                out = out.replace("{$i}", args[i]?.toString() ?: "null")
            }
        }
        return out
    }

    private fun applyLocked() {
        val tag = if (preference == SYSTEM) systemTag() else preference
        activeTag = tag
        dictionary = loadDictionary(tag)
    }

    private fun systemTag(): String {
        return try {
            val locales = Resources.getSystem().configuration.locales
            for (i in 0 until locales.size()) {
                val tag = when (locales[i].language) {
                    "pt" -> "pt-BR"
                    "en" -> "en"
                    "es" -> "es"
                    "ru" -> "ru"
                    "zh" -> "zh-CN"
                    "ja" -> "ja"
                    "ko" -> "ko"
                    else -> null
                }
                if (tag != null) return tag
            }
            FALLBACK_TAG
        } catch (_: Throwable) {
            FALLBACK_TAG
        }
    }

    private fun loadDictionary(tag: String): Map<String, String> {
        return try {
            val text = KenjinxApplication.context.assets.open("i18n/$tag.json")
                .bufferedReader(Charsets.UTF_8).use { it.readText() }
            val json = JSONObject(text)
            val map = HashMap<String, String>(json.length() * 2)
            val keys = json.keys()
            while (keys.hasNext()) {
                val key = keys.next()
                map[key] = json.getString(key)
            }
            map
        } catch (_: Throwable) {
            emptyMap()
        }
    }
}

/** Traduz um texto escrito em portugues (Brasil); {0}, {1}... recebem [args]. */
fun tr(text: String, vararg args: Any?): String = I18n.translate(text, args)
