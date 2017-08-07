declare module forgeImpl {
    var BigInteger: typeof jsbn.BigInteger;
}

declare module forge {
    
    export var jsbn: {
        BigInteger: typeof forgeImpl.BigInteger;
    };

    type ArrayBufferView = DataView|Int8Array|Uint8Array|Int16Array|Uint16Array|Int32Array|Uint32Array|Float32Array|Float64Array;

    module util {
        function setImmediate(func: Function): number;
        function nextTick(func: Function): number;
        function isArray(x: any): boolean;
        function isArrayBuffer(x: any): boolean;
        function isArrayBufferView(x: any): boolean;

        interface BufferInterface<T> {

            /**
             * Gets the number of bytes in this buffer.
             *
             * @return the number of bytes in this buffer.
             */
            length(): number;

            /**
             * Gets whether or not this buffer is empty.
             *
             * @return true if this buffer is empty, false if not.
             */
            isEmpty(): boolean;

            /**
             * Puts a byte in this buffer.
             *
             * @param b the byte to put.
             *
             * @return this buffer.
             */
            putByte(b: number): T;

            /**
             * Puts a byte in this buffer N times.
             *
             * @param b the byte to put.
             * @param n the number of bytes of value b to put.
             *
             * @return this buffer.
             */            
            fillWithByte(b: number, n: number): T;

            /**
             * Puts a string into this buffer.
             *
             * @param str the string to put.
             * @param [encoding] the encoding for the string (default: 'utf16').
             *
             * @return this buffer.
             */
            putString(str: string): T;

            /**
             * Puts a 16-bit integer in this buffer in big-endian order.
             *
             * @param i the 16-bit integer.
             *
             * @return this buffer.
             */
            putInt16(i: number): T;

            /**
             * Puts a 24-bit integer in this buffer in big-endian order.
             *
             * @param i the 24-bit integer.
             *
             * @return this buffer.
             */
            putInt24(i: number): T;
            
            /**
             * Puts a 32-bit integer in this buffer in big-endian order.
             *
             * @param i the 32-bit integer.
             *
             * @return this buffer.
             */
            putInt32(i: number): T;

            /**
             * Puts a 16-bit integer in this buffer in little-endian order.
             *
             * @param i the 16-bit integer.
             *
             * @return this buffer.
             */
            putInt16Le(i: number): T;

            /**
             * Puts a 24-bit integer in this buffer in little-endian order.
             *
             * @param i the 24-bit integer.
             *
             * @return this buffer.
             */            
            putInt24Le(i: number): T;

            /**
             * Puts a 32-bit integer in this buffer in little-endian order.
             *
             * @param i the 32-bit integer.
             *
             * @return this buffer.
             */
            putInt32Le(i: number): T;

            putInt(i: number, n: number): T;
            putSignedInt(i: number, n: number): T;

            getByte(): number;

            getInt16(): number;
            getInt24(): number;
            getInt32(): number;

            getInt16Le(): number;
            getInt24Le(): number;
            getInt32Le(): number;

            getInt(n: number): number;
            getSignedInt(n: number): number;

            getBytes(count?: number): string;
            bytes(count?: number): string;
            at(i: number): number;
            setAt(i: number, b: number): T;
            last(): number;
            copy(): T;

            compact(): T;
            clear(): T;
            truncate(count: number): T;
            toHex(): string;
            toString(encoding?: number): string;
        }

        interface ByteBuffer extends BufferInterface<ByteBuffer> {
            putBytes(bytes: string): ByteBuffer;

            /**
             * Puts the given buffer into this buffer.
             *
             * @param buffer the buffer to put into this one.
             *
             * @return this buffer.
             */
            putBuffer<T>(buffer: BufferInterface<T>): ByteBuffer;
        }

        interface ByteBufferStatic {
            new (b?: string|ArrayBuffer|ArrayBufferView|ByteBuffer): ByteBuffer;
        }

        var ByteBuffer: ByteBufferStatic;
        var ByteStringBuffer: ByteBufferStatic;

        type ByteBufferCompatible = string|number[]|util.ByteBuffer;

        interface DataBuffer extends BufferInterface<DataBuffer> {
            accommodate(amount: number, growSize?: number): DataBuffer;

            putBytes(bytes: string|DataBuffer|ArrayBuffer|ArrayBufferView, encoding?: string): DataBuffer;

            /**
             * Puts the given buffer into this buffer.
             *
             * @param buffer the buffer to put into this one.
             *
             * @return this buffer.
             */
            putBuffer<T>(bytes: BufferInterface<T>): DataBuffer;
        }

        interface DataBufferOptions {
            readOffset?: number;
            growSize?: number;
            writeOffset?: number;
            encoding?: string;
        }

        interface DataBufferStatic {
            new (b?: string|DataBuffer|ArrayBuffer|ArrayBufferView, options?: DataBufferOptions): DataBuffer;
        }

        var DataBuffer: DataBufferStatic;

        /**
         * Creates a buffer that stores bytes. A value may be given to put into the buffer that is
         * either a string of bytes or a UTF-16 string that will be encoded using UTF-8 (to do the
         * latter, specify 'utf8' as the encoding).
         * 
         * @param {string=} input    (Optional) the bytes to wrap (as a string) or a UTF-16 string to
         *                           encode as UTF-8.
         * @param {string=} encoding (Optional) (default: 'raw', other: 'utf8').
         *
         * @return {ByteBuffer} The new buffer.
         */
        function createBuffer(input?: string, encoding?: string): ByteBuffer;

        /**
         * Fills a string with a particular value. If you want the string to be a byte string, pass in
         * String.fromCharCode(theByte).
         *
         * @param {string} c the character to fill the string with, use String.fromCharCode to fill the
         *                   string with a byte value.
         * @param {number} n the number of characters of value c to fill with.
         *
         * @return {string} the filled string.
         */
        function fillString(c: string, n: number): string;

        /**
         * Performs a per byte XOR between two byte strings and returns the result as a string of bytes.
         *
         * @param {string} s1 first string of bytes.
         * @param {string} s2 second string of bytes.
         * @param {number} n  the number of bytes to XOR.
         *
         * @return {string} the XOR'd result.
         */
        function xorBytes(s1: string, s2: string, n: number): string;

        /**
         * Converts a hex string into a 'binary' encoded string of bytes.
         *
         * @param {string} hex the hexadecimal string to convert.
         *
         * @return {string} the binary-encoded string of bytes.
         */
        function hexToBytes(hex: string): string;

        /**
         * Converts a 'binary' encoded string of bytes to hex.
         *
         * @param {string} bytes the byte string to convert.
         *
         * @return {string} the string of hexadecimal characters.
         */
        function bytesToHex(bytes: string): string;

        /**
         * Converts an 32-bit integer to 4-big-endian byte string.
         *
         * @param {number} i the integer.
         *
         * @return {string} the byte string.
         */
        function int32ToBytes(i: number): string;

        /**
         * Base64 encodes a 'binary' encoded string of bytes.
         *
         * @param {string} input    the binary encoded string of bytes to base64-encode.
         * @param {number=} maxline (Optional) the maximum number of encoded characters per line to use,
         *                          defaults to none.
         *
         * @return {string} the base64-encoded output.
         */
        function encode64(input: string, maxline?: number): string;

        /**
         * Base64 decodes a string into a 'binary' encoded string of bytes.
         *
         * @param {string} input the base64-encoded input.
         *
         * @return {string} the binary encoded string.
         */
        function decode64(input: string): string;

        /**
         * UTF-8 encodes the given UTF-16 encoded string (a standard JavaScript string). Non-ASCII
         * characters will be encoded as multiple bytes according to UTF-8.
         *
         * @param {string} str the string to encode.
         *
         * @return {string} the UTF-8 encoded string.
         */
        function encodeUtf8(str: string): string;

        /**
         * Decodes a UTF-8 encoded string into a UTF-16 string.
         *
         * @param {string} str the string to decode.
         *
         * @return {string} the UTF-16 encoded string (standard JavaScript string).
         */
        function decodeUtf8(str: string): string;

        module binary {
            var raw: {
                encode(bytes: string): string;
                decode(str: string): Uint8Array;
                decode(str: string, output: Uint8Array, offset?: number): number;
            }
            var hex: {
                encode(bytes: string): string;
                decode(str: string): Uint8Array;
                decode(str: string, output: Uint8Array, offset?: number): number;
            }
            var base64: {
                encode(bytes: string, maxline?: number): string;
                decode(str: string): Uint8Array;
                decode(str: string, output: Uint8Array, offset?: number): number;
            }
        }
        module text {
            var utf8: {
                encode(str: string): Uint8Array;
                encode(str: string, output: Uint8Array, offset?: number): number;
                decode(bytes: string): string;
            }
            var utf16: {
                encode(str: string): Uint8Array;
                encode(str: string, output: Uint8Array, offset?: number): number;
                decode(bytes: string): string;
            }
        }

        interface FlashInterface {
            deflate(data: string): string;
            inflate(data: string): any;

            removeItem(id: string): void;
            setItem(id: string, obj: any): void;
            getItem(id: string): any;

            init: boolean;
        }

        function deflate(api: FlashInterface, bytes: string, raw: boolean): string;
        function inflate(api: FlashInterface, bytes: string, raw: boolean): string;

        function setItem(api: FlashInterface, id: string, key: string, data: Object, location: string[]): void;
        function getItem(api: FlashInterface, id: string, key: string, location: string[]): Object;
        function removeItem(api: FlashInterface, id: string, key: string, location: string[]): void;
        function clearItems(api: FlashInterface, id: string, location: string[]): void;

        interface URLParts {
            full: string;
            scheme: string;
            host: string;
            fullHost: string;
            port: number;
            path: string;
        }

        function parseUrl(str: string): URLParts;

        function getQueryVariables(query?: string): Object;

        interface FragmentParts {
            pathString: string;
            queryString: string;
            path: string[];
            query: Object;
        }

        function parseFragment(fragment: string): Object;

        interface Request {
            path: string;
            query: string;
            getPath(): string[];
            getPath(i: number): string;
            getQuery(): Object;
            getQuery(k: string): string[];
            getQuery(k: string, i: number): string;
            getQueryLast(k: string, _default?: string): string;
        }

        function makeRequest(reqString: string): Request;

        function makeLink(path: string|string[], query?: Object, fragment?: string): string;
        function setPath(object: Object, keys: string[], value: string): void;
        function getPath(object: Object, keys: string[], _default?: string): string;
        function deletePath(object: Object, keys: string[]): void;
        function isEmpty(object: Object): boolean;
        function format(format: string, v1?: any, v2?: any, v3?: any, v4?: any, v5?: any, v6?: any, v7?: any, v8?: any): string;
        function formatNumber(num: number, decimals?: number, dec_point?: string, thousands_sep?: string): string;
        function formatSize(size: number): string;
        function bytesFromIP(ip: string): ByteBuffer;
        function bytesFromIPv4(ip: string): ByteBuffer;
        function bytesFromIPv6(ip: string): ByteBuffer;
        function bytesToIP(bytes: ByteBuffer): string;
        function bytesToIPv4(bytes: ByteBuffer): string;
        function bytesToIPv6(bytes: ByteBuffer): string;

        interface EstimateCoresOptions {
            update?: boolean;
        }
        function estimateCores(options: EstimateCoresOptions, callback: (err: Error, max: number) => void): void;
    }

    interface Hash<T> {
        algorithm: string;
        blockLength: number;
        digestLength: number;
        messageLength: number;
        messageLength64: number[]; // array of 2 numbers

        start(): T;
        update(msg: string, encoding?: string): T;
        digest(): forge.util.ByteBuffer;
    }

    interface MD5 extends Hash<MD5> {
    }

    interface SHA1 extends Hash<SHA1> {
    }

    interface SHA224 extends Hash<SHA224> {
    }

    interface SHA256 extends Hash<SHA256> {
    }

    interface SHA384 extends Hash<SHA384> {
    }

    interface SHA512 extends Hash<SHA512> {
    }

    module md5 {
        function create(): MD5;
    }

    module sha1 {
        function create(): SHA1;
    }

    module sha224 {
        function create(): SHA224;
    }

    module sha256 {
        function create(): SHA256;
    }

    module sha384 {
        function create(): SHA384;
    }

    module sha512 {
        export import sha384 = forge.sha384;
        export import sha224 = forge.sha224;
        function create(): SHA512;
    }

    module md {
        module algorithms {
            export import md5 = forge.md5;
            export import sha1 = forge.sha1;
            export import sha256 = forge.sha256;
            export import sha384 = forge.sha384;
            export import sha512 = forge.sha512;
        }
        export import md5 = forge.md5;
        export import sha1 = forge.sha1;
        export import sha256 = forge.sha256;
        export import sha384 = forge.sha384;
        export import sha512 = forge.sha512;
    }

    interface MaskGenerator {
        generate(seed: string, maskLen: number): string;
    }

    interface MGF1 extends MaskGenerator {
    }

    module mgf1 {
        function create<T>(md: Hash<T>): MGF1;
    }

    module mgf {
        export import mgf1 = forge.mgf1;
    }

    interface HMAC {
        start(): void;
        start<T>(md: Hash<T>, key?: util.ByteBufferCompatible): void;
        start(md: string, key?: util.ByteBufferCompatible): void;

        update(bytes: string): void;

        getMac(): util.ByteBuffer;
    }

    module hmac {
        function create(): HMAC;
    }

    interface BlockCipherStartParams {
        output?: util.ByteBuffer;
        iv: string|number[]|util.ByteBuffer;
        additionalData?: string;
        tagLength?: number;
    }

    interface PaddingFunction {
        (blockSize: number, buffer: util.ByteBuffer, decrypt: boolean): boolean;
    }

    interface Cipher {
        output: util.ByteBuffer;
        mode: cipher.modes.BlockMode;
        start(options: BlockCipherStartParams): void;
        update(input: util.ByteBuffer): void;
        finish(pad?: PaddingFunction): boolean;
    }

    module cipher {
        interface AlgorithmsDictionary {
            [name: string]: modes.BlockModeFactory;
        }

        var algorithms: AlgorithmsDictionary;

        interface BlockCipherOptions {
            algorithm: string;
            key: string|number[]|util.ByteBuffer;
            decrypt: boolean;
        }

        class BlockCipher implements Cipher {
            constructor(options: BlockCipherOptions);

            output: util.ByteBuffer;
            mode: cipher.modes.BlockMode;
            start(options: BlockCipherStartParams): void;
            update(input: util.ByteBuffer): void;
            finish(pad?: PaddingFunction): boolean;
        }

        function createCipher(algorithm: string, key: string|number[]|util.ByteBuffer): BlockCipher;
        function createDecipher(algorithm: string, key: string|number[]|util.ByteBuffer): BlockCipher;

        function registerAlgorithm(name: string, algorithm: modes.BlockModeFactory): void;
        function getAlgorithm(name: string): modes.BlockModeFactory;

        module modes {
            interface BlockModeOptions {
                cipher: Cipher;
                blockSize: number;
            }

            interface EncryptionOptions {
                iv: string|number[]|util.ByteBuffer
            }

            interface BlockMode {
                name: string;
                cipher: Cipher;
                blockSize: number;
                start(options: EncryptionOptions): void;
                encrypt(input: util.ByteBuffer, output: util.ByteBuffer): void;
                decrypt(input: util.ByteBuffer, output: util.ByteBuffer): void;
            }

            interface BlockModeFactory {
                new (options: BlockModeOptions): BlockMode;
            }

            interface BlockModeFactoryT<T> {
                new (options: BlockModeOptions): BlockMode;
            }

            interface ECB extends BlockMode {
                pad(input: util.ByteBuffer, options: {}): boolean;
                unpad(input: util.ByteBuffer, options: { overflow: number }): boolean;
            }

            interface CBC extends BlockMode {
                pad(input: util.ByteBuffer, options: {}): boolean;
                unpad(input: util.ByteBuffer, options: { overflow: number }): boolean;
            }

            interface CFB extends BlockMode {
                afterFinish(output: util.ByteBuffer, options: { overflow: number }): boolean;
            }

            interface OFB extends BlockMode {
                afterFinish(output: util.ByteBuffer, options: { overflow: number }): boolean;
            }

            interface CTR extends BlockMode {
                afterFinish(output: util.ByteBuffer, options: { overflow: number }): boolean;
            }

            interface GCMEncryptionOptions extends EncryptionOptions {
                additionalData?: string;
                tagLength?: number;
                decrypt?: boolean;
                tag?: string;
            }

            interface GCM extends BlockMode {
                tag: util.ByteBuffer;

                start(options: GCMEncryptionOptions): void;
                afterFinish(output: util.ByteBuffer, options: { overflow: number; decrypt?: boolean }): boolean;

                multiply(x: number[], y: number[]): number[];
                pow(x: number[], y: number[]): number[];
                tableMultiply(x: number[]): number[];
                ghash(h: number[], y: number[], x: number[]): number[];
                generateHashTable(h: number[], bits: number): number[];
                generateSubHashTable(mid: number[], bits: number): number[];
            }

            var ecb: BlockModeFactoryT<ECB>;
            var cbc: BlockModeFactoryT<CBC>;
            var cfb: BlockModeFactoryT<CFB>;
            var ofb: BlockModeFactoryT<OFB>;
            var ctr: BlockModeFactoryT<CTR>;
            var gcm: BlockModeFactoryT<GCM>;
        }
    }

    module aes {
        interface sE12<T1, T2> {
            (key: T1, iv: T2, output: util.ByteBuffer, mode?: string): Cipher;
        }

        interface sE1<T> extends sE12<T, string>, sE12<T, number[]>, sE12<T, util.ByteBuffer> {
        }

        interface sE extends sE1<string>, sE1<number[]>, sE1<util.ByteBuffer> {
        }

        interface cEC1<T> {
            (key: T, mode?: string): Cipher;
        }

        interface cEC extends cEC1<string>, cEC1<number[]>, cEC1<util.ByteBuffer> {
        }

        var startEncrypting: sE;
        var startDecrypting: sE;
        var createEncryptionCipher: cEC;
        var createDecryptionCipher: cEC;

        interface Ai_T<T> {
            /**
             * Initializes this AES algorithm by expanding its key.
             */
            (options: { key: T; decrypt: boolean; }): void;
        }

        interface AlgorithmInitializeSignature extends Ai_T<string>, Ai_T<number[]>, Ai_T<util.ByteBuffer> {
        }

        class Algorithm {
            /**
             * Creates a new AES cipher algorithm object.
             *
             * @param name the name of the algorithm.
             * @param mode the mode factory function.
             *
             * @return the AES algorithm object.
             */
            constructor(name: string, mode: string);
            initialize: AlgorithmInitializeSignature;
        }

        /**
         * Expands a key. Typically only used for testing.
         *
         * @param {number[]} key    the symmetric key to expand, as an array of 32-bit words.
         * @param {boolean} decrypt true to expand for decryption, false for encryption.
         *
         * @return the expanded key.
         */
        function _expandKey(key: number[], decrypt: boolean): number[];

        /**
         * Updates a single block (16 bytes) using AES. The update will either encrypt or decrypt the
         * block.
         *
         * @param w       the key schedule.
         * @param input   the input block (an array of 32-bit words).
         * @param output  the updated output block.
         * @param decrypt true to decrypt the block, false to encrypt it.
         */
        function _updateBlock(w: number[], input: number[], output: number[], decrypt: boolean): void;
    }

    module prng {
        interface RandomCallback {
            (err: Error, bytes: string): void;
        }

        interface PseudoRendomGenerator {
            generate(count: number, callback: RandomCallback): void;
            generateSync(count: number): string;
        }

        function create(plugin: any/* TBD */): PseudoRendomGenerator;
    }

    interface Random extends prng.PseudoRendomGenerator {
        getBytes(count: number, callback: prng.RandomCallback): void;
        getBytesSync(count: number): string;
    }

    var random: Random;

    module rc2 {


        interface expandKey_T<T> {

            /**
             * Perform RC2 key expansion as per RFC #2268, section 2.
             *
             * @param {string|ByteBufer} key variable-length user key (between 1 and 128 bytes)
             * @param {number=} effKeyBits   (Optional) number of effective key bits (default: 128)
             *
             * @return the expanded RC2 key (ByteBuffer of 128 bytes)
             */
            (key: T, effKeyBits?: number): util.ByteBuffer;
        }

        interface expandKeySignature extends expandKey_T<string>, expandKey_T<util.ByteBuffer> {
        }

        var expandKey: expandKeySignature;

        interface CipherStart_T<T> {

            /**
             * Starts or restarts the encryption or decryption process, whichever was previously configured.
             * 
             * To use the cipher in CBC mode, iv may be given either as a string of bytes, or as a byte
             * buffer.  For ECB mode, give null as iv.
             *
             * @param {?string|ByteBuffer} iv (Optional) the initialization vector to use, null for ECB mode.
             * @param {ByteBuffer=} output    (Optional) the output the buffer to write to, null to create one.
             */
            (iv?: T, output?: util.ByteBuffer): void;
        }

        interface CipherStartSignature extends CipherStart_T<string>, CipherStart_T<util.ByteBuffer> {
        }

        interface Cipher {
            output: util.ByteBuffer;

            start: CipherStartSignature;

            /**
             * Updates the next block.
             *
             * @param input the buffer to read from.
             */
            update(input: util.ByteBuffer): void;

            /**
             * Finishes encrypting or decrypting.
             *
             * @param pad (Optional) a padding function to use, null for PKCS#7 padding, signature(blockSize,
             *            buffer, decrypt).
             *
             * @return true if successful, false on error.
             */
            finish(pad?: PaddingFunction): boolean;
        }

        interface createCipher_T<T> {

            /**
             * Creates a RC2 cipher object.
             *
             * @param {string|ByteBuffer} key the symmetric key to use (as base for key generation).
             * @param {number=} bits          (Optional) the number of effective key bits.
             * @param {boolean=} encrypt      (Optional) false for decryption, true for encryption.
             *
             * @return the cipher.
             */
            (key: T, bits?: number, encrypt?: boolean): Cipher;
        }

        interface createCipherSignature extends createCipher_T<string>, createCipher_T<util.ByteBuffer> {
        }

        var createCipher: createCipherSignature;

        interface startEncrypting_T12<T1, T2> {

            /**
             * Creates an RC2 cipher object to encrypt data in ECB or CBC mode using the given symmetric
             * key. The output will be stored in the 'output' member of the returned cipher.
             * 
             * The key and iv may be given as a string of bytes or a byte buffer. The cipher is initialized
             * to use 128 effective key bits.
             *
             * @param {string|ByteBuffer} key the symmetric key to use.
             * @param {string|ByteBuffer} iv  the initialization vector to use.
             * @param {ByteBuffer=} output    (Optional) the buffer to write to, null to create one.
             *
             * @return the cipher.
             */
            (key: T1, iv: T2, output?: util.DataBuffer): Cipher;
        }

        interface startEncrypting_T1<T1> extends startEncrypting_T12<T1, string>, startEncrypting_T12<T1, util.ByteBuffer> {
        }

        interface startEncryptingSignature extends startEncrypting_T1<string>, startEncrypting_T1<util.ByteBuffer> {
        }

        var startEncrypting: startEncryptingSignature;

        interface createEncryptionCipher_T<T> {

            /**
             * Creates an RC2 cipher object to encrypt data in ECB or CBC mode using the given symmetric key.
             * 
             * The key may be given as a string of bytes or a byte buffer.
             * 
             * To start encrypting call start() on the cipher with an iv and optional output buffer.
             *
             * @param {string|ByteBuffer} key the symmetric key to use.
             * @param {number=} bits          (Optional) The bits.
             *
             * @return the cipher.
             */
            (key: T, bits?: number): Cipher;
        }

        interface createEncryptionCipherSignature extends createEncryptionCipher_T<string>, createEncryptionCipher_T<util.ByteBuffer> {
        }

        var createEncryptionCipher: createEncryptionCipherSignature;

        interface startDecrypting_T12<T1, T2> {

            /**
             * Creates an RC2 cipher object to decrypt data in ECB or CBC mode using the given symmetric
             * key. The output will be stored in the 'output' member of the returned cipher.
             * 
             * The key and iv may be given as a string of bytes or a byte buffer. The cipher is initialized
             * to use 128 effective key bits.
             *
             * @param {string|ByteBuffer} key the symmetric key to use.
             * @param {string|ByteBuffer} iv  the initialization vector to use.
             * @param {ByteBuffer=} output    (Optional) the buffer to write to, null to create one.
             *
             * @return the cipher.
             */
            (key: T1, iv: T2, output?: util.ByteBuffer): Cipher;
        }

        interface startDecrypting_T1<T1> extends startDecrypting_T12<T1, string>, startDecrypting_T12<T1, util.ByteBuffer> {
        }

        interface startDecryptingSignature extends startDecrypting_T1<string>, startDecrypting_T1<util.ByteBuffer> {
        }

        var startDecrypting: startDecryptingSignature;

        interface createDecryptionCipher_T<T> {

            /**
             * Creates an RC2 cipher object to decrypt data in ECB or CBC mode using the given symmetric key.
             * 
             * The key may be given as a string of bytes or a byte buffer.
             * 
             * To start decrypting call start() on the cipher with an iv and optional output buffer.
             *
             * @param {string|ByteBuffer} key the symmetric key to use.
             * @param {number} bits           (Optional) the bits.
             *
             * @return the cipher.
             */
            (key: T, bits?: number): Cipher;
        }

        interface createDecryptionCipherSignature extends createDecryptionCipher_T<string>, createDecryptionCipher_T<util.ByteBuffer> {
        }

        var createDecryptionCipher: createDecryptionCipherSignature;
    }

    module rsa {

        /**
         * NOTE: THIS METHOD IS DEPRECATED, use 'sign' on a private key object or 'encrypt' on a public
         * key object instead.
         * 
         * Performs RSA encryption.
         * 
         * The parameter bt controls whether to put padding bytes before the message passed in. Set bt
         * to either true or false to disable padding completely (in order to handle e.g. EMSA-PSS
         * encoding seperately before), signaling whether the encryption operation is a public key
         * operation (i.e. encrypting data) or not, i.e. private key operation (data signing).
         * 
         * For PKCS#1 v1.5 padding pass in the block type to use, i.e. either 0x01 (for signing) or 0x02
         * (for encryption). The key operation mode (private or public) is derived from this flag in
         * that case).
         *
         * @param m   the message to encrypt as a byte string.
         * @param key the RSA key to use.
         * @param bt  for PKCS#1 v1.5 padding, the block type to use (0x01 for private key, 0x02 for
         *            public), to disable padding: true = public key, false = private key.
         *
         * @return the encrypted bytes as a string.
         */
        function encrypt(m: util.ByteBuffer/* TBD union */, key: util.ByteBuffer/* TBD union */, bt: any /* TBD type */): string;

        /**
         * NOTE: THIS METHOD IS DEPRECATED, use 'decrypt' on a private key object or 'verify' on a
         * public key object instead.
         * 
         * Performs RSA decryption.
         * 
         * The parameter ml controls whether to apply PKCS#1 v1.5 padding or not.  Set ml = false to
         * disable padding removal completely (in order to handle e.g. EMSA-PSS later on) and simply
         * pass back the RSA encryption block.
         *
         * @param ed  the encrypted data to decrypt in as a byte string.
         * @param key the RSA key to use.
         * @param pub true for a public key operation, false for private.
         * @param ml  the message length, if known, false to disable padding.
         *
         * @return the decrypted message as a byte string.
         */
        function decrypt(ed: util.ByteBuffer /* TBD union */, key: util.ByteBuffer /* TBD union */, pub: boolean, ml: number /* TBD boolean */): string;

        interface createKeyPairGenerationStateOptions {
            prng?: Random;
            algorithm?: string;
        }

        interface GenerationState {
        }

        /**
         * Creates an RSA key-pair generation state object. It is used to allow key-generation to be
         * performed in steps. It also allows for a UI to display progress updates.
         *
         * @param bits    (Optional) the size for the private key in bits, defaults to 2048.
         * @param e       (Optional) the public exponent to use, defaults to 65537 (0x10001).
         * @param options (Optional) the options to use. prng a custom crypto-secure pseudo-random number
         *                  generator to use, that must define "getBytesSync". algorithm the algorithm
         *                  to use (default: 'PRIMEINC').
         *
         * @return the state object to use to generate the key-pair.
         */
        function createKeyPairGenerationState(bits?: number, e?: number, options?: createKeyPairGenerationStateOptions): GenerationState;

        /**
         * Attempts to runs the key-generation algorithm for at most n seconds (approximately) using the
         * given state. When key-generation has completed, the keys will be stored in state.keys.
         * 
         * To use this function to update a UI while generating a key or to prevent causing browser
         * lockups/warnings, set "n" to a value other than 0. A simple pattern for generating a key and
         * showing a progress indicator is:
         * 
         * var state = pki.rsa.createKeyPairGenerationState(2048);
         * var step = function() {
         *   // step key-generation, run algorithm for 100 ms, repeat
         *   if(!forge.pki.rsa.stepKeyPairGenerationState(state, 100)) {
         *     setTimeout(step, 1);
         *   } else {
         *     // key-generation complete // TODO: turn off progress indicator here // TODO: use the
         *     generated key-pair in "state.keys"
         *   }
         * };
         * // TODO: turn on progress indicator here setTimeout(step, 0);
         *
         * @param state      the state to use.
         * @param {number} n the maximum number of milliseconds to run the algorithm for, 0 to run the
         *                   algorithm to completion.
         *
         * @return true if the key-generation completed, false if not.
         */
        function stepKeyPairGenerationState(state: GenerationState, n: number): boolean;

        interface EncryptSchemeOptions {
        }

        interface PrivateKey {
            n: jsbn.BigInteger;
            e: jsbn.BigInteger;
            d: jsbn.BigInteger;
            p: jsbn.BigInteger;
            q: jsbn.BigInteger;
            dP: jsbn.BigInteger;
            dQ: jsbn.BigInteger;
            qInv: jsbn.BigInteger;

            /**
             * Decrypts the given data with this private key. The decryption scheme must match the one used
             * to encrypt the data.
             *
             * @param {string} data   the byte string to decrypt.
             * @param {string} scheme (Optional) the decryption scheme to use: 'RSAES-PKCS1-V1_5' (default),
             *                        'RSA-OAEP', 'RAW', 'NONE', or null to perform raw RSA decryption.
             * @param schemeOptions   (Optional) any scheme-specific options.
             *
             * @return the decrypted byte string.
             */
            decrypt(data: string, scheme?: string, schemeOptions?: EncryptSchemeOptions): string;

            /**
             * Signs the given digest, producing a signature.
             * 
             * PKCS#1 supports multiple (currently two) signature schemes: RSASSA-PKCS1-V1_5 and RSASSA-PSS.
             * 
             * By default this implementation uses the "old scheme", i.e. RSASSA-PKCS1-V1_5. In order to
             * generate a PSS signature, provide an instance of Forge PSS object as the scheme parameter.
             *
             * @param md     the message digest object with the hash to sign.
             * @param scheme (Optional) the signature scheme to use: 'RSASSA-PKCS1-V1_5' or undefined for
             *               RSASSA PKCS#1 v1.5, a Forge PSS object for RSASSA-PSS, 'NONE' or null for none,
             *               DigestInfo will not be used but PKCS#1 v1.5 padding will still be used.
             *
             * @return the signature as a byte string.
             */
            sign(md: Hash<any>, scheme?: string): string;
        }

        interface PublicKey {
            n: jsbn.BigInteger;
            e: jsbn.BigInteger;

            /**
             * Encrypts the given data with this public key. Newer applications should use the 'RSA-OAEP'
             * decryption scheme, 'RSAES-PKCS1-V1_5' is for legacy applications.
             *
             * @param {string} data    the byte string to encrypt.
             * @param {string=} scheme (Optional) the encryption scheme to use: 'RSAES-PKCS1-V1_5' (default),
             *                         'RSA- OAEP', 'RAW', 'NONE', or null to perform raw RSA encryption, an
             *                         object with an 'encode' property set to a function with the signature
             *                         'function(data, key)' that returns a binary-encoded string
             *                         representing the encoded data.
             * @param schemeOptions    (Optional) any scheme-specific options.
             *
             * @return the encrypted byte string.
             */
            encrypt(data: string, scheme?: string, schemeOptions?: EncryptSchemeOptions): string;

            /**
             * Verifies the given signature against the given digest.
             * 
             * PKCS#1 supports multiple (currently two) signature schemes: RSASSA-PKCS1-V1_5 and RSASSA-PSS.
             * 
             * By default this implementation uses the "old scheme", i.e. RSASSA-PKCS1-V1_5, in which case
             * once RSA-decrypted, the signature is an OCTET STRING that holds a DigestInfo.
             * 
             * DigestInfo ::= SEQUENCE {
             *   digestAlgorithm DigestAlgorithmIdentifier, digest Digest
             * }
             * DigestAlgorithmIdentifier ::= AlgorithmIdentifier Digest ::= OCTET STRING
             * 
             * To perform PSS signature verification, provide an instance of Forge PSS object as the scheme
             * parameter.
             *
             * @param digest    the message digest hash to compare against the signature, as a binary-encoded
             *                  string.
             * @param signature the signature to verify, as a binary-encoded string.
             * @param scheme    (Optional) signature verification scheme to use: 'RSASSA-PKCS1-V1_5' or
             *                  undefined for RSASSA PKCS#1 v1.5, a Forge PSS object for RSASSA-PSS, 'NONE'
             *                  or null for none, DigestInfo will not be expected, but PKCS#1 v1.5 padding
             *                  will still be used.
             *
             * @return true if the signature was verified, false if not.
             */
            verify(digest: string, signature: string, scheme?: string): boolean;
        }

        interface KeyPair {
            privateKey: PrivateKey;
            publicKey: PublicKey;
        }

        interface KeyPairGenerationOptions {

            /**
             * the size for the private key in bits, (default: 2048).
             */
            bits?: number;

            /**
             * the public exponent to use, (default: 65537 (0x10001)).
             */
            e?: number;

            /**
             * the worker script URL.
             */
            workerScript?: string;

            /**
             * the number of web workers (if supported) to use, (default: 2).
             */
            workers?: number;

            /**
             * the size of the work load, ie: number of possible prime numbers for each web worker to check
             * per work assignment, (default: 100).
             */
            workLoad?: number;

            /**
             * a custom crypto-secure pseudo-random number generator to use, that must define "getBytesSync".
             */
            prng?: Random;

            /**
             * the algorithm to use (default: 'PRIMEINC').
             */
            algorithm?: string;
        }

        interface KeyPairGenerationCallback {
            (err: Error, keypair: KeyPair): void;
        }

        /**
         * Generates an RSA public-private key pair in a single call.
         * 
         * To generate a key-pair in steps (to allow for progress updates and to prevent blocking or
         * warnings in slow browsers) then use the key-pair generation state functions.
         * 
         * To generate a key-pair asynchronously (either through web-workers, if available, or by
         * breaking up the work on the main thread), pass a callback function.
         *
         * @param options be given:
         *                - bits the size for the private key in bits, (default: 2048).
         *                - e the public exponent to use, (default: 65537 (0x10001)).
         *                - workerScript the worker script URL.
         *                - workers the number of web workers (if supported) to use (default: 2).
         *                - workLoad the size of the work load, ie: number of possible prime numbers
         *                for each web worker to check per work assignment, (default: 100).
         *                - prng a custom crypto-secure pseudo-random number generator to use, that
         *                must define "getBytesSync".
         *                - algorithm the algorithm to use (default: 'PRIMEINC').
         *
         * @return an object with privateKey and publicKey properties.
         */

        // jsdoc for theese function can be written when typescript gets better

        function generateKeyPair(options: KeyPairGenerationOptions): KeyPair;
        function generateKeyPair(bits: number): KeyPair;
        function generateKeyPair(callback: KeyPairGenerationCallback): void;

        function generateKeyPair(bits: number, e: number): KeyPair;
        function generateKeyPair(bits: number, options: KeyPairGenerationOptions): KeyPair;
        function generateKeyPair(bits: number, callback: KeyPairGenerationCallback): void;
        function generateKeyPair(options: KeyPairGenerationOptions, callback: KeyPairGenerationCallback): void;

        function generateKeyPair(bits: number, e: number, options: KeyPairGenerationOptions): KeyPair;
        function generateKeyPair(bits: number, e: number, callback: KeyPairGenerationCallback): void;
        function generateKeyPair(bits: number, options: KeyPairGenerationOptions, callback: KeyPairGenerationCallback): void;

        function generateKeyPair(bits: number, e: number, options: KeyPairGenerationOptions, callback: KeyPairGenerationCallback): void;

        /**
         * Sets an RSA public key from BigIntegers modulus and exponent.
         *
         * @param {BigInteger} n the modulus.
         * @param {BigInteger} e the exponent.
         *
         * @return the public key.
         */
        function setPublicKey(n: jsbn.BigInteger, e: jsbn.BigInteger): PublicKey;

        /**
         * Sets an RSA private key from BigIntegers modulus, exponent, primes, prime exponents, and
         * modular multiplicative inverse.
         *
         * @param {BigInteger} n    the modulus.
         * @param {BigInteger} e    the public exponent.
         * @param {BigInteger} d    the private exponent ((inverse of e) mod n).
         * @param {BigInteger} p    the first prime.
         * @param {BigInteger} q    the second prime.
         * @param {BigInteger} dP   exponent1 (d mod (p-1)).
         * @param {BigInteger} dQ   exponent2 (d mod (q-1)).
         * @param {BigInteger} qInv ((inverse of q) mod p)
         *
         * @return the private key.
         */
        function setPrivateKey(n: jsbn.BigInteger, e: jsbn.BigInteger, d: jsbn.BigInteger, p: jsbn.BigInteger, q: jsbn.BigInteger, dP: jsbn.BigInteger, dQ: jsbn.BigInteger, qInv: jsbn.BigInteger): PrivateKey;
    }

    /**
     * Sets an RSA public key from BigIntegers modulus and exponent.
     *
     * @param {BigInteger} n the modulus.
     * @param {BigInteger} e the exponent.
     *
     * @return the public key.
     */
    function setRsaPublicKey(n: jsbn.BigInteger, e: jsbn.BigInteger): rsa.PublicKey;

    /**
     * Sets an RSA private key from BigIntegers modulus, exponent, primes, prime exponents, and
     * modular multiplicative inverse.
     *
     * @param {BigInteger} n    the modulus.
     * @param {BigInteger} e    the public exponent.
     * @param {BigInteger} d    the private exponent ((inverse of e) mod n).
     * @param {BigInteger} p    the first prime.
     * @param {BigInteger} q    the second prime.
     * @param {BigInteger} dP   exponent1 (d mod (p-1)).
     * @param {BigInteger} dQ   exponent2 (d mod (q-1)).
     * @param {BigInteger} qInv ((inverse of q) mod p)
     *
     * @return the private key.
     */
    function setRsaPrivateKey(n: jsbn.BigInteger, e: jsbn.BigInteger, d: jsbn.BigInteger, p: jsbn.BigInteger, q: jsbn.BigInteger, dP: jsbn.BigInteger, dQ: jsbn.BigInteger, qInv: jsbn.BigInteger): rsa.PrivateKey;

    module pki {
        export import rsa = forge.rsa;

        /**
         * Converts a private key from an ASN.1 object.
         *
         * @param obj the ASN.1 representation of a PrivateKeyInfo containing an RSAPrivateKey or an
         *            RSAPrivateKey.
         *
         * @return the private key.
         */
        function wrapRsaPrivateKey(obj: any): rsa.PrivateKey; // TODO: maybe it is possible to specify ASN.1

        /**
         * Converts a private key to an ASN.1 RSAPrivateKey.
         *
         * @param key the private key.
         *
         * @return the ASN.1 representation of an RSAPrivateKey.
         */
        function privateKeyToAsn1(key: rsa.PrivateKey): any; // TODO: maybe it is possible to specify ASN.1

        /**
         * Converts a private key to an ASN.1 RSAPrivateKey.
         *
         * @param key the private key.
         *
         * @return the ASN.1 representation of an RSAPrivateKey.
         */
        function privateKeyToRSAPrivateKey(key: rsa.PrivateKey): any; // TODO: maybe it is possible to specify ASN.1

        /**
         * Converts a public key from an ASN.1 SubjectPublicKeyInfo or RSAPublicKey.
         *
         * @param obj the asn1 representation of a SubjectPublicKeyInfo or RSAPublicKey.
         *
         * @return the public key.
         */
        function publicKeyFromAsn1(obj: any): rsa.PublicKey; // TODO: maybe it is possible to specify ASN.1

        /**
         * Converts a public key to an ASN.1 SubjectPublicKeyInfo.
         *
         * @param key the public key.
         *
         * @return the asn1 representation of a SubjectPublicKeyInfo.
         */
        function publicKeyToAsn1(key: rsa.PublicKey): any; // TODO: maybe it is possible to specify ASN.1

        /**
         * Converts a public key to an ASN.1 SubjectPublicKeyInfo.
         *
         * @param key the public key.
         *
         * @return the asn1 representation of a SubjectPublicKeyInfo.
         */
        function publicKeyToSubjectPublicKeyInfo(key: rsa.PublicKey): any; // TODO: maybe it is possible to specify ASN.1

        /**
         * Converts a public key to an ASN.1 RSAPublicKey.
         *
         * @param key the public key.
         *
         * @return the asn1 representation of a RSAPublicKey.
         */
        function publicKeyToRSAPublicKey(key: rsa.PublicKey): any; // TODO: maybe it is possible to specify ASN.1
    }

    module pss {

        /**
         * Creates a PSS signature scheme object.
         * 
         * There are several ways to provide a salt for encoding:
         * 
         * - Specify the saltLength only and the built-in PRNG will generate it.
         * - Specify the saltLength and a custom PRNG with 'getBytesSync' defined that will be used.
         * - Specify the salt itself as a forge.util.ByteBuffer.
         *
         * @param options the options to use
         *                - md the message digest object to use, a forge md instance.
         *                - mgf the mask generation function to use, a forge mgf instance.
         *                - {number} saltLength the length of the salt in octets.
         *                - prng the pseudo-random number generator to use to produce a salt.
         *                - salt the salt to use when encoding.
         *
         * @return a signature scheme object.
         */
        function create(options: any /* TBD */): any;

        /**
         * @see forge.rsa.publicKeyToAsn1
         */
        function create<T>(md: Hash<T>, mgf: MaskGenerator, saltLength: number): any;
    }

    module ssh {
        /**
         * Encodes (and optionally encrypts) a private RSA key as a Putty PPK file.
         *
         * @param privateKey the key.
         * @param passphrase a passphrase to protect the key (falsy for no encryption).
         * @param comment a comment to include in the key file.
         *
         * @return the PPK file as a string.
         */
        function privateKeyToPutty(privateKey: forge.rsa.PrivateKey, passphrase?: string, comment?: string): string;

        /**
         * Encodes a public RSA key as an OpenSSH file.
         *
         * @param key the key.
         * @param comment a comment.
         *
         * @return the public key in OpenSSH format.
         */
        function publicKeyToOpenSSH(key: forge.rsa.PublicKey, comment?: string): string;

        /**
         * Encodes a private RSA key as an OpenSSH file.
         *
         * @param key the key.
         * @param passphrase a passphrase to protect the key (falsy for no encryption).
         *
         * @return the public key in OpenSSH format.
         */
        function privateKeyToOpenSSH(privateKey: forge.rsa.PrivateKey, passphrase?: string): string;

        type FingerprintOptions = {
            md?: Hash<any>;
            encoding?: string;
            delimiter?: string;
        };

        /**
         * Gets the SSH fingerprint for the given public key.
         *
         * @param options the options to use.
         *          [md] the message digest object to use (defaults to forge.md.md5).
         *          [encoding] an alternative output encoding, such as 'hex'
         *            (defaults to none, outputs a byte buffer).
         *          [delimiter] the delimiter to use between bytes for 'hex' encoded
         *            output, eg: ':' (defaults to none).
         *
         * @return the fingerprint as a byte buffer or other encoding based on options.
         */
        function getPublicKeyFingerprint(key: forge.rsa.PublicKey, options?: FingerprintOptions): string;
    }

    module pkcs5 {
        export function pbkdf2(): any;
    }
}

declare module "node-forge" {
    export = forge;
}
